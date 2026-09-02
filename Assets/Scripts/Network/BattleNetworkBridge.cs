using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;

// Handles all network RPCs for the battle scene.
// Lives on a spawned prefab (NOT scene-placed). BattleController stays a plain MonoBehaviour.
// Server subscribes to TurnManager events and broadcasts results to clients.
// Clients send ServerRpcs for all player actions; server runs the logic and broadcasts back.
public class BattleNetworkBridge : NetworkBehaviour
{
    public static BattleNetworkBridge Instance  { get; private set; }
    public static bool               IsServer   { get; private set; }

    private void Awake()
    {
        Instance = this;
        // Detects OUR OWN connection to the host dropping (host crashed/quit). Subscribed here
        // (not OnStartClient/OnStopClient) so it's tied to connection state directly, matching
        // how LobbyUI detects connect — not to this NetworkBehaviour's own spawn lifecycle.
        InstanceFinder.ClientManager.OnClientConnectionState += OnClientConnectionState;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (InstanceFinder.ClientManager != null)
            InstanceFinder.ClientManager.OnClientConnectionState -= OnClientConnectionState;
    }

    // [DISCONNECT HANDLING] We lost our own connection to the host mid-battle. There's no host
    // migration and no one left to declare a winner — just leave rather than sitting on a frozen
    // screen with no way out. The host itself always has a local client connection too (it starts
    // both), but its own client "stopping" is just a side effect of being the host, not a real
    // disconnect — IsServerStarted excludes that case.
    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        if (IsServerStarted) return;
        if (args.ConnectionState != LocalConnectionState.Stopped) return;

        MatchSetup.ResetForNewMatch();
        // Fully qualified: NetworkBehaviour has its own inherited "SceneManager" instance
        // property (FishNet's scene manager, a different API entirely) that would otherwise
        // shadow the UnityEngine.SceneManagement one reached via the using directive.
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

    // ── FishNet callbacks ──────────────────────────────────────────────────

    // Buffers the fighter whose turn just ended until OnActivationChanged fires
    // (at which point ActiveTeamId has already been updated to the next team).
    private Fighter _pendingTurnEndFighter;

    public override void OnStartServer()
    {
        base.OnStartServer();
        IsServer = true;
        // Activation is broadcast via an explicit call in CmdActivateFighter, not a subscription
        // here — see the comment there for why (it used to race PassiveManager/TileEffectManager,
        // which are also subscribers to OnFighterActivated, with no guaranteed ordering).
        TurnManager.OnFighterTurnEnded  += OnServerFighterTurnEnded;
        TurnManager.OnRoundStarted      += OnServerRoundStarted;
        TurnManager.OnActivationChanged += OnServerActivationChanged;
        TurnManager.OnGameOver          += OnServerGameOver;
        BattleLogger.OnEntry            += OnServerBattleLog;
        InstanceFinder.ServerManager.OnRemoteConnectionState += OnServerRemoteConnectionState;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        IsServer = false;
        TurnManager.OnFighterTurnEnded  -= OnServerFighterTurnEnded;
        TurnManager.OnRoundStarted      -= OnServerRoundStarted;
        TurnManager.OnActivationChanged -= OnServerActivationChanged;
        TurnManager.OnGameOver          -= OnServerGameOver;
        BattleLogger.OnEntry            -= OnServerBattleLog;
        InstanceFinder.ServerManager.OnRemoteConnectionState -= OnServerRemoteConnectionState;
    }

    // [DISCONNECT HANDLING] The remote client (always team 2) dropped mid-battle — forfeit them
    // rather than leaving the host waiting forever on a fighter that can never act again.
    // ForceGameOver no-ops if the game already ended normally, so this is safe even if the
    // disconnect happens right as/after a real win.
    private void OnServerRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState != RemoteConnectionState.Stopped) return;
        TurnManager.Instance?.ForceGameOver(1);
    }

    // ── Server event handlers → broadcast ────────────────────────────────

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
        BroadcastBattleState();
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

        BroadcastBattleState();
        RpcRoundStarted(roundNumber, TurnManager.Instance.ActiveTeamId);
    }

    private void OnServerBattleLog(LogEntry entry)
    {
        RpcBattleLog(entry.Message, (int)entry.Category);
    }

    private void OnServerGameOver(int winningTeamId)
    {
        RpcGameOver(winningTeamId);
    }

    // ── Broadcast helpers (called from BattleController after actions) ────

    // The one broadcast call site everything routes through — deliberately bundles fighter state
    // AND tile effects into a single method rather than two separate ones, so no call site can
    // forget to sync one of them (that's exactly the class of bug that left tile effects
    // completely unsynced to clients until now).
    //
    // Fighter states: serializes every fighter's whole FighterState (JsonUtility.ToJson) rather
    // than picking individual fields — see FighterState.cs. Ability cooldowns ride along here too
    // (folded into State.AbilityCooldowns just before serializing) instead of a separate RPC.
    //
    // Tile effects: serializes TileEffectManager's full current set (flattened, see
    // TileEffectSnapshotEntry) the same way — wholesale, not incremental.
    public void BroadcastBattleState()
    {
        var fighters = FighterManager.Instance.AllFighters;
        int n = fighters.Count;
        var names      = new string[n];
        var stateJsons = new string[n];

        for (int i = 0; i < n; i++)
        {
            var f = fighters[i];
            f.State.AbilityCooldowns = f.Abilities.Select(a => a.CurrentCooldown).ToList();
            names[i]      = f.FighterName;
            stateJsons[i] = JsonUtility.ToJson(f.State);
        }

        RpcSyncFighterStates(names, stateJsons);

        if (TileEffectManager.Instance != null)
        {
            var wire = new TileEffectsWireFormat { Entries = TileEffectManager.Instance.CaptureSnapshot() };
            RpcSyncTileEffects(JsonUtility.ToJson(wire));
        }
    }

    // ── ServerRpcs (client → server) ──────────────────────────────────────

    // Host always acts via BattleController's local branch (see RequestMove/RequestUseAbility/etc.
    // — they only route to a CmdX call when MatchSetup.Mode == Online && !IsServer), so any of
    // these ServerRpcs that actually reach the server is guaranteed to have come from the sole
    // remote client, which is always team 2. Guards below reject anything for the other team.
    private const int RemoteClientTeamId = 2;

    [ServerRpc(RequireOwnership = false)]
    public void CmdActivateFighter(string fighterName)
    {
        var fighter = FighterManager.Instance.GetFighterByName(fighterName);
        if (fighter == null || fighter.TeamId != RemoteClientTeamId) return;
        TurnManager.Instance.ActivateFighter(fighter);

        // Broadcast AFTER ActivateFighter() returns, as a plain sequential call — not as a
        // subscriber racing PassiveManager/TileEffectManager on the same OnFighterActivated
        // event (their relative subscription order isn't guaranteed by Unity). By the time this
        // line runs, any passive/tile-effect reactions triggered by the activation have already
        // completed synchronously, so the broadcast is guaranteed to include them.
        //
        // If the fighter died to a DoT tick at activation, ActivateFighter() falls through to
        // EndFighterTurn() instead of actually activating — that path broadcasts on its own via
        // OnServerFighterTurnEnded/OnServerActivationChanged, so skip the redundant broadcast here.
        if (!fighter.IsDead)
        {
            BroadcastBattleState();
            RpcFighterActivated(fighter.FighterName);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void CmdRequestMove(string fighterName, Vector2Int destination)
    {
        var fighter = FighterManager.Instance.GetFighterByName(fighterName);
        if (fighter == null || fighter.TeamId != RemoteClientTeamId) return;
        // Fire-and-forget: RequestMove now steps the path over real time (see ProgressiveResolver)
        // and this ServerRpc can't be async — its per-step broadcasts are what actually deliver
        // the result back to clients, not this call returning.
        BattleController.Instance.RequestMove(fighter, destination).Forget();
    }

    [ServerRpc(RequireOwnership = false)]
    public void CmdRequestAbility(string fighterName, string abilityName,
                                  Vector2Int anchor, Vector2Int[] shapeTiles)
    {
        var fighter = FighterManager.Instance.GetFighterByName(fighterName);
        if (fighter == null || fighter.TeamId != RemoteClientTeamId) return;
        var ability = fighter.Abilities.FirstOrDefault(a => a.Name == abilityName);
        if (ability == null) return;
        BattleController.Instance.RequestUseAbility(fighter, ability, anchor,
                                                    new List<Vector2Int>(shapeTiles));
    }

    [ServerRpc(RequireOwnership = false)]
    public void CmdRequestSecondaryEffect(string fighterName, string abilityName,
                                          Vector2Int anchor, Vector2Int[] shapeTiles)
    {
        var fighter = FighterManager.Instance.GetFighterByName(fighterName);
        if (fighter == null || fighter.TeamId != RemoteClientTeamId) return;
        var ability = fighter.Abilities.FirstOrDefault(a => a.Name == abilityName);
        if (ability == null) return;
        BattleController.Instance.RequestUseSecondaryEffect(fighter, ability, anchor,
                                                             new List<Vector2Int>(shapeTiles));
    }

    [ServerRpc(RequireOwnership = false)]
    public void CmdRequestReposition(string fighterName, Vector2Int destination)
    {
        var fighter = FighterManager.Instance.GetFighterByName(fighterName);
        if (fighter == null || fighter.TeamId != RemoteClientTeamId) return;
        BattleController.Instance.RequestReposition(fighter, destination);
    }

    [ServerRpc(RequireOwnership = false)]
    public void CmdRequestEndTurn()
    {
        // No specific fighter passed here, so gate on whose turn it actually is instead.
        if (TurnManager.Instance == null || TurnManager.Instance.ActiveTeamId != RemoteClientTeamId) return;
        BattleController.Instance.RequestEndTurn();
        // OnFighterTurnEnded subscription handles broadcast
    }

    // [DEBUG] See DebugCommands.cs / DebugConsole.cs — testing-only, not team-gated.
    [ServerRpc(RequireOwnership = false)]
    public void CmdDebugCommand(string command)
    {
        string result = DebugCommands.Execute(command);
        Debug.Log($"[DebugConsole] (from client) '{command}' -> {result}");
        BroadcastBattleState();
    }

    // ── ObserverRpcs (server → clients, excluding server) ─────────────────

    [ObserversRpc(ExcludeServer = true)]
    private void RpcSyncFighterStates(string[] names, string[] stateJsons)
    {
        var board = BattleController.Instance.GetBoard();
        for (int i = 0; i < names.Length; i++)
        {
            var fighter = FighterManager.Instance.GetFighterByName(names[i]);
            if (fighter == null) continue;

            var newState = JsonUtility.FromJson<FighterState>(stateJsons[i]);
            fighter.ApplyNetworkState(newState, board);

            // Keep the move-range display live if this is our currently selected fighter —
            // covers movement, and is a harmless no-op refresh for any other state change.
            var sm = SelectionManager.Instance;
            if (sm != null && sm.SelectedFighter == fighter)
            {
                sm.FireMovePointsChanged(fighter.RemainingMovePoints, fighter.Speed);
                if (fighter.RemainingMovePoints <= 0f)
                    sm.ExitMoveMode();
                else if (sm.InMoveMode)
                    sm.RefreshMoveRange();
            }
        }
    }

    [ObserversRpc(ExcludeServer = true)]
    private void RpcSyncTileEffects(string wireJson)
    {
        var wire = JsonUtility.FromJson<TileEffectsWireFormat>(wireJson);
        TileEffectManager.Instance?.ApplyNetworkSnapshot(wire.Entries);
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
    private void RpcBattleLog(string message, int category)
    {
        BattleLogger.Log(message, (LogCategory)category);
    }

    [ObserversRpc(ExcludeServer = true)]
    private void RpcGameOver(int winningTeamId)
    {
        TurnManager.Instance.NetworkApplyGameOver(winningTeamId);
    }

    // ── Scene transition ───────────────────────────────────────────────────

    // Called by GameOverHandler only when BattleNetworkBridge.IsServer — same pattern as
    // CharSelectNetworkBridge.ServerStartBattle. Only the server triggers the load; FishNet
    // propagates it to every connected client. A client calling SceneManager.LoadScene directly
    // instead would bypass FishNet's own scene/object teardown and risk leaving the
    // NetworkManager's spawned-object registry in a bad state.
    public void ServerLoadScene(string sceneName)
    {
        if (!IsServerStarted) return;
        var data = new FishNet.Managing.Scened.SceneLoadData(sceneName);
        data.ReplaceScenes = FishNet.Managing.Scened.ReplaceOption.All;
        InstanceFinder.NetworkManager.SceneManager.LoadGlobalScenes(data);
    }
}
