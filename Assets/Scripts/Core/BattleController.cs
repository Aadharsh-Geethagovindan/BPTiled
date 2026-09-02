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

        TurnManager.OnFighterActivated += HandleFighterActivated;
    }

    private void OnDestroy()
    {
        TurnManager.OnFighterActivated -= HandleFighterActivated;
    }

    public Board GetBoard() => board;

    // A stunned fighter can't act — log it, hold briefly (no stun visuals/animation yet), then end
    // their turn the same way a real End Turn click would. Not gated to server/hotseat-only: this
    // just reacts to already-synced state and calls the normal RequestEndTurn gateway, which itself
    // already routes correctly for both hotseat and online (and no-ops if the turn already ended).
    private void HandleFighterActivated(Fighter fighter)
    {
        if (!fighter.State.StatusEffects.Exists(e => e.Type == StatusEffectType.Stun)) return;

        BattleLogger.Log($"{fighter.FighterName} is stunned, they cannot act on this turn.", LogCategory.Effect);
        AutoEndStunnedTurn(fighter).Forget();
    }

    private async UniTaskVoid AutoEndStunnedTurn(Fighter fighter)
    {
        await UniTask.Delay(1500);
        if (turnManager.ActiveFighter == fighter)
            RequestEndTurn();
    }

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

    // Steps the fighter through the path one tile at a time (see MoveResolver/ProgressiveResolver)
    // instead of teleporting to the destination — each tile's state mutation (position, cost,
    // any tile effect triggered) commits and broadcasts before the next one, so remote viewers
    // and the local UI both see it resolve in step with the per-tile animation, not all at once.
    public async UniTask RequestMove(Fighter fighter, Vector2Int destination)
    {
        if (fighter != turnManager.ActiveFighter) return;
        if (fighter.IsDead) return;
        if (fighter.RemainingMovePoints <= 0f) return;

        if (MatchSetup.Mode == GameMode.Online && !BattleNetworkBridge.IsServer)
        {
            // Just forward the request — the server steps the path and broadcasts each tile
            // back via BroadcastBattleState; this client applies them (and animates) as they
            // arrive through ApplyNetworkState, same as any other synced state change.
            BattleNetworkBridge.Instance?.CmdRequestMove(fighter.FighterName, destination);
            return;
        }

        var path = MoveResolver.GetPath(fighter, destination, board);
        if (path == null || path.Count == 0) return;

        bool online = MatchSetup.Mode == GameMode.Online;
        fighter.SetMoving(true);

        await ProgressiveResolver.RunSteps(path, tile =>
        {
            MoveResolver.StepTile(fighter, tile, board);
            if (online)
                BattleNetworkBridge.Instance?.BroadcastBattleState();
            SelectionManager.Instance?.FireMovePointsChanged(fighter.RemainingMovePoints, fighter.Speed);
        }, MoveResolver.StepDurationMs, () => fighter.IsDead || fighter.RemainingMovePoints <= 0f);

        fighter.SetMoving(false);
        fighter.SetMoved(true);

        if (online)
            BattleNetworkBridge.Instance?.BroadcastBattleState();
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
            BattleNetworkBridge.Instance?.BroadcastBattleState();

        // A CanMoveAfterAction fighter's turn only truly ends once she's out of movement, not
        // merely because she'd already moved before acting (see Sedra: move-then-act shouldn't
        // forfeit the move-after-act her passive grants).
        bool repositionPending = ability.RepositionRange > 0;
        bool secondaryPending  = ability.SecondaryEffect != null;
        if (!repositionPending && !secondaryPending && (!fighter.CanMoveAfterAction || fighter.RemainingMovePoints <= 0f))
            turnManager.EndFighterTurn();
    }

    // Resolves an ability's SecondaryEffect once its own follow-up target has been picked (e.g.
    // Vemk Parlas's Sig, after the player chooses which ally receives the transferred buffs).
    // Mirrors RequestReposition's scope: the cooldown/charge-reset/acted-flag side effects already
    // happened on the primary RequestUseAbility call, so this only resolves the one deferred effect
    // and then applies the same end-of-turn check that a non-deferred ability would have used.
    public void RequestUseSecondaryEffect(Fighter fighter, Ability ability, Vector2Int anchor, List<Vector2Int> shapeTiles)
    {
        if (MatchSetup.Mode == GameMode.Online && !BattleNetworkBridge.IsServer)
        {
            BattleNetworkBridge.Instance?.CmdRequestSecondaryEffect(
                fighter.FighterName, ability.Name, anchor, shapeTiles.ToArray());
            return;
        }

        var secondaryEffect = ability.SecondaryEffect;
        if (secondaryEffect == null) return;

        AbilityResolver.Execute(fighter, ability, shapeTiles, board,
                                onlyEffect: secondaryEffect, grantChargeAndFireEvent: false);

        if (MatchSetup.Mode == GameMode.Online)
            BattleNetworkBridge.Instance?.BroadcastBattleState();

        if (!fighter.CanMoveAfterAction || fighter.RemainingMovePoints <= 0f)
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
            BattleNetworkBridge.Instance?.BroadcastBattleState();

        if (!fighter.CanMoveAfterAction || fighter.RemainingMovePoints <= 0f)
            turnManager.EndFighterTurn();
    }

    public void RequestEndTurn()
    {
        if (turnManager.ActiveFighter == null) return;
        // Refuse while the active fighter is still stepping through a move (see Fighter.IsMoving)
        // — this is the one action not routed through SelectionManager.HandleTileClicked's own
        // IsMoving gate (its button lives outside tile-click input), so guard it here instead,
        // at the request layer, where it covers every caller regardless of UI wiring.
        if (turnManager.ActiveFighter.IsMoving) return;

        if (MatchSetup.Mode == GameMode.Online && !BattleNetworkBridge.IsServer)
        {
            BattleNetworkBridge.Instance?.CmdRequestEndTurn();
            return;
        }

        turnManager.EndFighterTurn();
    }

    // [DEBUG] Testing-only command entry point — see DebugCommands.cs and DebugConsole.cs.
    // Deliberately not team-gated like the real request methods above; it's a shared testing
    // tool for whoever's driving the session, not a gameplay action either player "owns".
    public string RequestDebugCommand(string command)
    {
        if (MatchSetup.Mode == GameMode.Online && !BattleNetworkBridge.IsServer)
        {
            BattleNetworkBridge.Instance?.CmdDebugCommand(command);
            return $"Sent: {command}";
        }

        string result = DebugCommands.Execute(command);

        if (MatchSetup.Mode == GameMode.Online)
            BattleNetworkBridge.Instance?.BroadcastBattleState();

        return result;
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