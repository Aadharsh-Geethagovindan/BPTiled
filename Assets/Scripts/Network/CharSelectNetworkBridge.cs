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

    private void Awake()     { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    // ── FishNet callbacks ──────────────────────────────────────────────────

    public override void OnStartServer()
    {
        base.OnStartServer();
        //Debug.Log("[Bridge] OnStartServer fired");

        CharSelectManager.IsServer = true;
        MatchSetup.LocalTeamId = 1; // host controls Team 1

        var mgr = CharSelectManager.Instance;
        if (mgr == null) { Debug.LogError("[Bridge] CharSelectManager not found"); return; }

        mgr.LocalTeamIndex = 0;

        int seed = Random.Range(1, int.MaxValue);
        mgr.GenerateMapWithSeed(seed);
        mgr.StartDraftInternal();

        RpcGenerateMap(seed);
        RpcSyncReset(mgr.FirstPickTeamIndex);

        CharSelectManager.OnPickMade      += OnServerPickMade;
        CharSelectManager.OnDraftComplete += OnServerDraftComplete;
        //Debug.Log("[Bridge] OnStartServer complete — subscribed to OnPickMade");
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        CharSelectManager.OnPickMade      -= OnServerPickMade;
        CharSelectManager.OnDraftComplete -= OnServerDraftComplete;
        CharSelectManager.IsServer = false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!IsServerStarted)
        {
            CharSelectManager.IsServer = false;
            MatchSetup.LocalTeamId = 2; // client controls Team 2
            StartCoroutine(SetLocalTeamIndexWhenReady());

            CmdRequestState();
        }
    }

    // CharSelectManager.Instance can still be null the instant this NetworkObject spawns on the
    // client (Awake order between scene objects isn't guaranteed). Leaving LocalTeamIndex stuck
    // at its -1 default is worse than a null check skipping the assignment — CharSelectUI treats
    // -1 as "always my turn" (the hotseat convention), which lets the client confirm picks out
    // of turn until something else happens to re-trigger this. Retry until it's actually set.
    private System.Collections.IEnumerator SetLocalTeamIndexWhenReady()
    {
        yield return new WaitUntil(() => CharSelectManager.Instance != null);
        CharSelectManager.Instance.LocalTeamIndex = 1;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
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
        Debug.Log($"[Bridge] TargetRpcReceiveState — seed={mapSeed} CSM={CharSelectManager.Instance != null}");
        StartCoroutine(ApplyStateWhenReady(mapSeed, firstPickTeamIndex));
    }

    private System.Collections.IEnumerator ApplyStateWhenReady(int mapSeed, int firstPickTeamIndex)
    {
        yield return new WaitUntil(() => CharSelectManager.Instance != null);
        Debug.Log($"[Bridge] ApplyStateWhenReady — applying seed={mapSeed}");
        CharSelectManager.Instance.GenerateMapWithSeed(mapSeed);
        CharSelectManager.Instance.ApplyResetFromNetwork(firstPickTeamIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    public void CmdTryPick(string fighterName)
    {
        var mgr = CharSelectManager.Instance;
        if (mgr == null) return;

        // Host always picks via the local ProcessPick() path (see CharSelectManager.TryPick), so
        // any CmdTryPick that reaches the server came from the sole remote client — team index 1.
        // Reject it if it isn't actually that team's turn (client-side gating in CharSelectUI is
        // cosmetic only and shouldn't be trusted, especially while LocalTeamIndex is still
        // settling right after connecting).
        if (mgr.ActiveTeamIndex != 1) return;

        mgr.ProcessPick(fighterName);
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
        //Debug.Log($"[Bridge] OnServerPickMade — team={teamIdx} fighter={fighter?.name}");
        var mgr = CharSelectManager.Instance;
        if (mgr == null) return;

        bool done = mgr.DraftComplete;
        RpcPickMade(teamIdx, fighter.name, mgr.PickNumber, done,
                    done ? MatchSetup.Team1Fighters : null,
                    done ? MatchSetup.Team2Fighters : null,
                    done ? MatchSetup.FirstActingTeam : 0);
        //Debug.Log("[Bridge] RpcPickMade sent");
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
        //Debug.Log($"[Bridge] RpcPickMade received on client — team={teamIdx} fighter={fighterName}");
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
