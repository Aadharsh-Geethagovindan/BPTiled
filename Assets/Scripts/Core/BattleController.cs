using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

public enum BattleState
{
    Idle,
    BoardSetup,
    Draft,
    RoundStart,
    ActivationSelect,
    MovementPhase,
    ActionPhase,
    ResolutionPhase,
    RoundEnd,
    GameOver
}

public class BattleController : MonoBehaviour
{
    public static BattleController Instance { get; private set; }

    private BattleState _currentState = BattleState.Idle;
    public BattleState CurrentState => _currentState;

    [SerializeField] private float animationTimeout = 3f;
    [SerializeField] private Board board;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private CameraController cameraController;
    [SerializeField] private TerrainGenerator terrainGenerator;
    [SerializeField] private FighterManager   fighterManager;
    [SerializeField] private TileHighlighter  tileHighlighter;
    [SerializeField] private SelectionManager selectionManager;
    [SerializeField] private TurnManager      turnManager;
    [SerializeField] private PassiveManager     passiveManager;
    [SerializeField] private TileEffectManager  tileEffectManager;
    [SerializeField] private TurnTrackerPanel turnTrackerPanel;
    [SerializeField] private BalanceSettings  balanceSettings;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public Board GetBoard() => board;

    private void Start()
    {
        InitializeBoard();
    }

    private void InitializeBoard()
    {
        board.Initialize();
        terrainGenerator.Initialize(board);
        if (MatchSetup.MapSeed != 0)
            terrainGenerator.SetSeed(MatchSetup.MapSeed);
        terrainGenerator.Generate();
        boardRenderer.Initialize(board);
        tileHighlighter.Initialize(board);
        selectionManager.Initialize(board, tileHighlighter);
        cameraController.FitToBoard(board);

        fighterManager.Initialize(board);

        // Fallback: if no BalanceSettings asset assigned, use defaults (all multipliers neutral)
        if (balanceSettings == null)
            balanceSettings = ScriptableObject.CreateInstance<BalanceSettings>();

        var roster = FighterLoader.LoadRoster();

        // Use MatchSetup if coming from character select, otherwise fall back to debug defaults
        var team1Names = MatchSetup.IsReady
            ? MatchSetup.Team1Fighters
            : new[] { "Jack", "Avarice", "Sanguine" };
        var team2Names = MatchSetup.IsReady
            ? MatchSetup.Team2Fighters
            : new[] { "Krakoa", "Captain Dinso", "Vas Drel" };

        // Spawn positions spread evenly along each team's back row
        var team1Positions = new[] { new Vector2Int(2, 6), new Vector2Int(4, 6), new Vector2Int(6, 6) };
        var team2Positions = new[] { new Vector2Int(2, 1), new Vector2Int(4, 1), new Vector2Int(6, 1) };

        for (int i = 0; i < team1Names.Length; i++)
            if (roster.TryGetValue(team1Names[i], out var data))
                fighterManager.SpawnFighterFromData(data, 1, team1Positions[i], balanceSettings);
            else
                Debug.LogWarning($"[BattleController] Fighter '{team1Names[i]}' not found in roster");

        for (int i = 0; i < team2Names.Length; i++)
            if (roster.TryGetValue(team2Names[i], out var data))
                fighterManager.SpawnFighterFromData(data, 2, team2Positions[i], balanceSettings);
            else
                Debug.LogWarning($"[BattleController] Fighter '{team2Names[i]}' not found in roster");

        tileEffectManager.Initialize();
        passiveManager.Initialize(fighterManager, board);
        turnManager.Initialize(fighterManager);
        turnManager.StartRound();

        turnTrackerPanel.Initialize(fighterManager, turnManager);

        Debug.Log("[BattleController] Board setup complete");
    }

    // ── [CLIENT → SERVER] Request gateway ──────────────────────────────────
    // For Phase 1 (hotseat) these are called directly by client-side code.
    // For Phase 3 (multiplayer) these become [ServerRpc] — logic unchanged.
    // Rule: client code NEVER mutates Fighter state directly. Always goes here.

    public void RequestMove(Fighter fighter, Vector2Int destination)
    {
        if (fighter != turnManager.ActiveFighter) return;
        if (fighter.IsDead) return;
        if (fighter.RemainingMovePoints <= 0f) return;

        if (MatchSetup.Mode == GameMode.Online && !BattleNetworkBridge.IsServer)
        {
            BattleNetworkBridge.Instance?.CmdRequestMove(fighter.FighterName, destination);
            return;
        }

        if (MoveResolver.ExecuteMove(fighter, destination, board))
        {
            fighter.SetMoved(true);
            if (MatchSetup.Mode == GameMode.Online)
                BattleNetworkBridge.Instance?.BroadcastFighterMoved(
                    fighter.FighterName, fighter.GridPosition, fighter.RemainingMovePoints);
        }
    }

    public void RequestUseAbility(Fighter fighter, Ability ability, Vector2Int anchor, List<Vector2Int> shapeTiles)
    {
        if (fighter != turnManager.ActiveFighter) return;
        if (fighter.IsDead) return;
        if (fighter.HasActedThisTurn) return;
        if (ability.IsOnCooldown) return;
        if (ability.Slot == AbilitySlot.Sig && fighter.CurrentCharge < fighter.SigChargeReq) return;

        if (MatchSetup.Mode == GameMode.Online && !BattleNetworkBridge.IsServer)
        {
            BattleNetworkBridge.Instance?.CmdRequestAbility(
                fighter.FighterName, ability.Name, anchor, shapeTiles.ToArray());
            return;
        }

        AbilityResolver.Execute(fighter, ability, shapeTiles, board);

        if (ability.MovesUser && !ability.SwapWithTarget)
            MoveResolver.ExecuteReposition(fighter, anchor, board);

        if (ability.BaseCooldown > 0)
            ability.SetCooldown();

        if (ability.Slot == AbilitySlot.Sig)
            fighter.ResetCharge();

        fighter.SetActed(true);

        if (MatchSetup.Mode == GameMode.Online)
        {
            BattleNetworkBridge.Instance?.BroadcastAllFighterStates();
            if (ability.BaseCooldown > 0)
                BattleNetworkBridge.Instance?.BroadcastAbilityCooldown(fighter, ability);
        }

        bool repositionPending = ability.RepositionRange > 0;
        if (!repositionPending && (!fighter.CanMoveAfterAction || fighter.HasMovedThisTurn))
            turnManager.EndFighterTurn();
    }

    public void RequestReposition(Fighter fighter, Vector2Int destination)
    {
        if (MatchSetup.Mode == GameMode.Online && !BattleNetworkBridge.IsServer)
        {
            BattleNetworkBridge.Instance?.CmdRequestReposition(fighter.FighterName, destination);
            return;
        }

        MoveResolver.ExecuteReposition(fighter, destination, board);

        if (MatchSetup.Mode == GameMode.Online)
            BattleNetworkBridge.Instance?.BroadcastFighterMoved(
                fighter.FighterName, fighter.GridPosition, fighter.RemainingMovePoints);

        if (!fighter.CanMoveAfterAction || fighter.HasMovedThisTurn)
            turnManager.EndFighterTurn();
    }

    public void RequestEndTurn()
    {
        if (turnManager.ActiveFighter == null) return;

        if (MatchSetup.Mode == GameMode.Online && !BattleNetworkBridge.IsServer)
        {
            BattleNetworkBridge.Instance?.CmdRequestEndTurn();
            return;
        }

        turnManager.EndFighterTurn();
    }

    // ── [SERVER] Battle flow stubs — will become ServerRpcs in Phase 3 ────────

    public async UniTaskVoid StartBattle()
    {
        await TransitionTo(BattleState.BoardSetup);
        await RunBoardSetup();

        await TransitionTo(BattleState.Draft);
        await RunDraft();

        await RunMatchLoop();
    }

    private async UniTask RunMatchLoop()
    {
        while (!IsGameOver())
        {
            await TransitionTo(BattleState.RoundStart);
            await RunRoundStart();

            await TransitionTo(BattleState.ActivationSelect);
            await RunActivationSelect();

            await TransitionTo(BattleState.MovementPhase);
            await RunMovementPhase();

            await TransitionTo(BattleState.ActionPhase);
            await RunActionPhase();

            await TransitionTo(BattleState.ResolutionPhase);
            await RunResolutionPhase();

            await TransitionTo(BattleState.RoundEnd);
            await RunRoundEnd();
        }

        await TransitionTo(BattleState.GameOver);
        await RunGameOver();
    }

    private async UniTask RunBoardSetup()
    {
        board.Initialize();
        boardRenderer.Initialize(board);
        Debug.Log("[BattleController] Board setup complete");
        await UniTask.CompletedTask;
    }

    private async UniTask RunDraft() { await UniTask.CompletedTask; }
    private async UniTask RunRoundStart() { await UniTask.CompletedTask; }
    private async UniTask RunActivationSelect() { await UniTask.CompletedTask; }
    private async UniTask RunMovementPhase() { await UniTask.CompletedTask; }
    private async UniTask RunActionPhase() { await UniTask.CompletedTask; }
    private async UniTask RunResolutionPhase() { await UniTask.CompletedTask; }
    private async UniTask RunRoundEnd() { await UniTask.CompletedTask; }
    private async UniTask RunGameOver() { await UniTask.CompletedTask; }

    private async UniTask TransitionTo(BattleState newState)
    {
        _currentState = newState;
        Debug.Log($"[BattleController] State -> {newState}");
        await UniTask.CompletedTask;
    }

    private bool IsGameOver()
    {
        return false;
    }

    public async UniTask PlayWithTimeout(UniTask animationTask, float timeout = -1f)
    {
        float t = timeout < 0 ? animationTimeout : timeout;
        await UniTask.WhenAny(animationTask, UniTask.Delay((int)(t * 1000)));
    }
}