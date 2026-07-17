using System.Collections.Generic;
using System.Linq;
using FishNet.Object;
using UnityEngine;

// Handles all network RPCs for the battle scene.
// Lives on a spawned prefab (NOT scene-placed). BattleController stays a plain MonoBehaviour.
// Server subscribes to TurnManager events and broadcasts results to clients.
// Clients send ServerRpcs for all player actions; server runs the logic and broadcasts back.
public class BattleNetworkBridge : NetworkBehaviour
{
    public static BattleNetworkBridge Instance  { get; private set; }
    public static bool               IsServer   { get; private set; }

    private void Awake()     { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    // ── FishNet callbacks ──────────────────────────────────────────────────

    // Buffers the fighter whose turn just ended until OnActivationChanged fires
    // (at which point ActiveTeamId has already been updated to the next team).
    private Fighter _pendingTurnEndFighter;

    public override void OnStartServer()
    {
        base.OnStartServer();
        IsServer = true;
        TurnManager.OnFighterActivated  += OnServerFighterActivated;
        TurnManager.OnFighterTurnEnded  += OnServerFighterTurnEnded;
        TurnManager.OnRoundStarted      += OnServerRoundStarted;
        TurnManager.OnActivationChanged += OnServerActivationChanged;
        BattleLogger.OnEntry            += OnServerBattleLog;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        IsServer = false;
        TurnManager.OnFighterActivated  -= OnServerFighterActivated;
        TurnManager.OnFighterTurnEnded  -= OnServerFighterTurnEnded;
        TurnManager.OnRoundStarted      -= OnServerRoundStarted;
        TurnManager.OnActivationChanged -= OnServerActivationChanged;
        BattleLogger.OnEntry            -= OnServerBattleLog;
    }

    // ── Server event handlers → broadcast ────────────────────────────────

    private void OnServerFighterActivated(Fighter fighter)
    {
        // Sync state first (DoT may have changed HP; ResetTurnState updated move points)
        BroadcastAllFighterStates();
        RpcFighterActivated(fighter.FighterName);
    }

    private void OnServerFighterTurnEnded(Fighter fighter)
    {
        // Buffer the fighter — ActiveTeamId hasn't been updated to next team yet.
        // OnServerActivationChanged fires after ActiveTeamId is set and sends the RPC.
        _pendingTurnEndFighter = fighter;
    }

    // Fires after ActiveTeamId is updated (either normal turn rotation or round start).
    private void OnServerActivationChanged(int nextTeamId)
    {
        if (_pendingTurnEndFighter == null) return;
        var fighter = _pendingTurnEndFighter;
        _pendingTurnEndFighter = null;
        BroadcastAllFighterStates();
        RpcTurnEnded(fighter.FighterName, nextTeamId);
    }

    private void OnServerRoundStarted(int roundNumber)
    {
        if (roundNumber == 1) return; // both sides init round 1 independently

        // The round-ending fighter's turn-end hasn't been flushed yet (StartRound() runs
        // synchronously inside EndFighterTurn(), before OnActivationChanged gets a chance to
        // consume the buffer) — flush it now so clients still get OnFighterTurnEnded/Deselect
        // for that fighter instead of silently losing it.
        if (_pendingTurnEndFighter != null)
        {
            var fighter = _pendingTurnEndFighter;
            _pendingTurnEndFighter = null;
            RpcTurnEnded(fighter.FighterName, TurnManager.Instance.ActiveTeamId);
        }

        BroadcastAllFighterStates();
        RpcRoundStarted(roundNumber, TurnManager.Instance.ActiveTeamId);
    }

    private void OnServerBattleLog(LogEntry entry)
    {
        RpcBattleLog(entry.Message, (int)entry.Category);
    }

    // ── Broadcast helpers (called from BattleController after actions) ────

    public void BroadcastFighterMoved(string fighterName, Vector2Int newPos, float remainingMovePoints)
    {
        RpcFighterMoved(fighterName, newPos.x, newPos.y, remainingMovePoints);
    }

    public void BroadcastAllFighterStates()
    {
        var fighters = FighterManager.Instance.AllFighters;
        int n = fighters.Count;
        var names      = new string[n];
        var hps        = new int[n];
        var charges    = new int[n];
        var posXs      = new int[n];
        var posYs      = new int[n];
        var hasActed   = new bool[n];
        var hasMoved   = new bool[n];
        var hasActvtd  = new bool[n];
        var movePoints = new float[n];

        for (int i = 0; i < n; i++)
        {
            var f      = fighters[i];
            names[i]      = f.FighterName;
            hps[i]        = f.CurrentHP;
            charges[i]    = f.CurrentCharge;
            posXs[i]      = f.GridPosition.x;
            posYs[i]      = f.GridPosition.y;
            hasActed[i]   = f.HasActedThisTurn;
            hasMoved[i]   = f.HasMovedThisTurn;
            hasActvtd[i]  = f.HasActivatedThisRound;
            movePoints[i] = f.RemainingMovePoints;
        }

        RpcSyncFighterStates(names, hps, charges, posXs, posYs, hasActed, hasMoved, hasActvtd, movePoints);
    }

    public void BroadcastAbilityCooldown(Fighter fighter, Ability ability)
    {
        RpcSyncAbilityCooldown(fighter.FighterName, ability.Name, ability.CurrentCooldown);
    }

    // ── ServerRpcs (client → server) ──────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    public void CmdActivateFighter(string fighterName)
    {
        var fighter = FighterManager.Instance.GetFighterByName(fighterName);
        if (fighter == null) return;
        TurnManager.Instance.ActivateFighter(fighter);
        // OnFighterActivated subscription handles broadcast
    }

    [ServerRpc(RequireOwnership = false)]
    public void CmdRequestMove(string fighterName, Vector2Int destination)
    {
        var fighter = FighterManager.Instance.GetFighterByName(fighterName);
        if (fighter == null) return;
        BattleController.Instance.RequestMove(fighter, destination);
    }

    [ServerRpc(RequireOwnership = false)]
    public void CmdRequestAbility(string fighterName, string abilityName,
                                  Vector2Int anchor, Vector2Int[] shapeTiles)
    {
        var fighter = FighterManager.Instance.GetFighterByName(fighterName);
        if (fighter == null) return;
        var ability = fighter.Abilities.FirstOrDefault(a => a.Name == abilityName);
        if (ability == null) return;
        BattleController.Instance.RequestUseAbility(fighter, ability, anchor,
                                                    new List<Vector2Int>(shapeTiles));
    }

    [ServerRpc(RequireOwnership = false)]
    public void CmdRequestReposition(string fighterName, Vector2Int destination)
    {
        var fighter = FighterManager.Instance.GetFighterByName(fighterName);
        if (fighter == null) return;
        BattleController.Instance.RequestReposition(fighter, destination);
    }

    [ServerRpc(RequireOwnership = false)]
    public void CmdRequestEndTurn()
    {
        BattleController.Instance.RequestEndTurn();
        // OnFighterTurnEnded subscription handles broadcast
    }

    // ── ObserverRpcs (server → clients, excluding server) ─────────────────

    [ObserversRpc(ExcludeServer = true)]
    private void RpcSyncFighterStates(
        string[] names, int[] hps, int[] charges,
        int[] posXs, int[] posYs,
        bool[] hasActed, bool[] hasMoved, bool[] hasActivated,
        float[] movePoints)
    {
        var board = BattleController.Instance.GetBoard();
        for (int i = 0; i < names.Length; i++)
        {
            var fighter = FighterManager.Instance.GetFighterByName(names[i]);
            if (fighter == null) continue;
            fighter.NetworkApplyHP(hps[i]);
            fighter.NetworkApplyCharge(charges[i]);
            fighter.NetworkApplyPosition(new Vector2Int(posXs[i], posYs[i]), board);
            fighter.SetActed(hasActed[i]);
            fighter.SetMoved(hasMoved[i]);
            fighter.SetActivated(hasActivated[i]);
            fighter.NetworkApplyRemainingMovePoints(movePoints[i]);
        }
    }

    [ObserversRpc(ExcludeServer = true)]
    private void RpcFighterActivated(string fighterName)
    {
        var fighter = FighterManager.Instance.GetFighterByName(fighterName);
        if (fighter == null) return;

        TurnManager.Instance.NetworkApplyActivation(fighter);

        // If this is our fighter, wire up the SelectionManager so action buttons work
        if (MatchSetup.LocalTeamId != 0 && fighter.TeamId == MatchSetup.LocalTeamId)
            SelectionManager.Instance?.NetworkApplyActivation(fighter);
    }

    [ObserversRpc(ExcludeServer = true)]
    private void RpcFighterMoved(string fighterName, int x, int y, float remainingMovePoints)
    {
        var fighter = FighterManager.Instance.GetFighterByName(fighterName);
        if (fighter == null) return;

        fighter.NetworkApplyPosition(new Vector2Int(x, y), BattleController.Instance.GetBoard());
        fighter.NetworkApplyRemainingMovePoints(remainingMovePoints);

        // Update move range display if this is our currently selected fighter
        var sm = SelectionManager.Instance;
        if (sm != null && sm.SelectedFighter == fighter)
        {
            sm.FireMovePointsChanged(fighter.RemainingMovePoints, fighter.Speed);
            if (remainingMovePoints <= 0f)
                sm.ExitMoveMode();
            else if (sm.InMoveMode)
                sm.RefreshMoveRange();
        }
    }

    [ObserversRpc(ExcludeServer = true)]
    private void RpcTurnEnded(string fighterName, int nextActiveTeamId)
    {
        var fighter = FighterManager.Instance.GetFighterByName(fighterName);
        if (fighter == null) return;

        TurnManager.Instance.NetworkApplyTurnEnded(fighter, nextActiveTeamId);

        // Deselect if we had this fighter selected
        if (SelectionManager.Instance?.SelectedFighter == fighter)
            SelectionManager.Instance.Deselect();
    }

    [ObserversRpc(ExcludeServer = true)]
    private void RpcRoundStarted(int roundNumber, int firstTeamId)
    {
        TurnManager.Instance.NetworkApplyRoundStarted(roundNumber, firstTeamId);
    }

    [ObserversRpc(ExcludeServer = true)]
    private void RpcSyncAbilityCooldown(string fighterName, string abilityName, int cooldown)
    {
        var fighter = FighterManager.Instance.GetFighterByName(fighterName);
        if (fighter == null) return;
        var ability = fighter.Abilities.FirstOrDefault(a => a.Name == abilityName);
        if (ability != null) ability.CurrentCooldown = cooldown;
    }

    [ObserversRpc(ExcludeServer = true)]
    private void RpcBattleLog(string message, int category)
    {
        BattleLogger.Log(message, (LogCategory)category);
    }
}
