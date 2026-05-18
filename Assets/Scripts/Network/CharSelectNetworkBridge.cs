using FishNet;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

// Handles all network RPCs for CharSelect.
// Lives on its own GameObject with NetworkObject. CharSelectManager stays a plain MonoBehaviour.
public class CharSelectNetworkBridge : NetworkBehaviour
{
    public static CharSelectNetworkBridge Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private string battleSceneName = "BattleScene";

    private void OnEnable()  { Instance = this; }
    private void OnDisable() { if (Instance == this) Instance = null; }

    // ── FishNet callbacks ──────────────────────────────────────────────────

    public override void OnStartServer()
    {
        base.OnStartServer();

        CharSelectManager.IsNetworked = true;
        CharSelectManager.IsServer    = true;

        var mgr = CharSelectManager.Instance;
        if (mgr == null) { Debug.LogError("[Bridge] CharSelectManager not found"); return; }

        mgr.LocalTeamIndex = 0;

        int seed = Random.Range(1, int.MaxValue);
        mgr.GenerateMapWithSeed(seed);
        mgr.StartDraftInternal();

        // Push map seed and initial draft state to clients
        RpcGenerateMap(seed);
        RpcSyncReset(mgr.FirstPickTeamIndex);

        // Subscribe to manager events to know when to broadcast
        CharSelectManager.OnPickMade      += OnServerPickMade;
        CharSelectManager.OnDraftComplete += OnServerDraftComplete;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        CharSelectManager.OnPickMade      -= OnServerPickMade;
        CharSelectManager.OnDraftComplete -= OnServerDraftComplete;
        CharSelectManager.IsNetworked = false;
        CharSelectManager.IsServer    = false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        CharSelectManager.IsNetworked = true;

        if (!IsServerStarted)
        {
            CharSelectManager.IsServer = false;
            if (CharSelectManager.Instance != null)
                CharSelectManager.Instance.LocalTeamIndex = 1;

            // Request current state from server — RpcGenerateMap may have fired before we were ready
            CmdRequestState();
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (!IsServerStarted)
            CharSelectManager.IsNetworked = false;
    }

    // ── Server RPCs (client → server) ─────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void CmdRequestState(NetworkConnection conn = null)
    {
        var mgr = CharSelectManager.Instance;
        if (mgr == null) return;
        TargetRpcReceiveState(conn, MatchSetup.MapSeed, mgr.FirstPickTeamIndex);
    }

    [TargetRpc]
    private void TargetRpcReceiveState(NetworkConnection conn, int mapSeed, int firstPickTeamIndex)
    {
        CharSelectManager.Instance?.GenerateMapWithSeed(mapSeed);
        CharSelectManager.Instance?.ApplyResetFromNetwork(firstPickTeamIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    public void CmdTryPick(string fighterName)
    {
        CharSelectManager.Instance?.ProcessPick(fighterName);
    }

    [ServerRpc(RequireOwnership = false)]
    public void CmdResetDraft()
    {
        var mgr = CharSelectManager.Instance;
        if (mgr == null) return;
        mgr.ProcessReset();
        RpcSyncReset(mgr.FirstPickTeamIndex);
    }

    // ── Server event handlers → broadcast ─────────────────────────────────

    private void OnServerPickMade(int teamIdx, FighterData fighter)
    {
        var mgr = CharSelectManager.Instance;
        if (mgr == null) return;

        bool done = mgr.DraftComplete;
        RpcPickMade(teamIdx, fighter.name, mgr.PickNumber, done,
                    done ? MatchSetup.Team1Fighters : null,
                    done ? MatchSetup.Team2Fighters : null,
                    done ? MatchSetup.FirstActingTeam : 0);
    }

    private void OnServerDraftComplete()
    {
        // Server already handled locally; clients get the flag via RpcPickMade
    }

    // ── Observer RPCs (server → clients, excluding server) ─────────────────

    [ObserversRpc(ExcludeServer = true)]
    private void RpcGenerateMap(int seed)
    {
        CharSelectManager.Instance?.GenerateMapWithSeed(seed);
    }

    [ObserversRpc(ExcludeServer = true)]
    private void RpcSyncReset(int firstPickTeamIndex)
    {
        CharSelectManager.Instance?.ApplyResetFromNetwork(firstPickTeamIndex);
    }

    [ObserversRpc(ExcludeServer = true)]
    private void RpcPickMade(int teamIdx, string fighterName, int newPickNumber,
                             bool draftComplete, string[] t1, string[] t2, int firstActing)
    {
        CharSelectManager.Instance?.ApplyPickFromNetwork(
            teamIdx, fighterName, newPickNumber, draftComplete, t1, t2, firstActing);
    }

    // ── Scene transition ───────────────────────────────────────────────────

    public void ServerStartBattle()
    {
        if (!IsServerStarted) return;
        var data = new FishNet.Managing.Scened.SceneLoadData(battleSceneName);
        data.ReplaceScenes = FishNet.Managing.Scened.ReplaceOption.All;
        InstanceFinder.NetworkManager.SceneManager.LoadGlobalScenes(data);
    }
}
