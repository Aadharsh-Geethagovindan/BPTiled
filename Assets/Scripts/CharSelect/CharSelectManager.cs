using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CharSelectManager : MonoBehaviour
{
    public static CharSelectManager Instance { get; private set; }

    [Header("Scene refs")]
    [SerializeField] private Board            board;
    [SerializeField] private TerrainGenerator terrainGenerator;
    [SerializeField] private BoardRenderer    boardRenderer;
    [SerializeField] private CameraController cameraController;

    [Header("Settings")]
    [SerializeField] private string battleSceneName = "BattleScene";
    [SerializeField] private bool   restrictionsEnabled = true;

    [Header("UI")]
    [SerializeField] private CharSelectUI ui;

    // ── Network state ──────────────────────────────────────────────────────

    public static bool IsNetworked => MatchSetup.Mode == GameMode.Online;
    public static bool IsServer    { get; set; } = false;
    public int LocalTeamIndex { get; set; } = -1;

    // ── Draft state ────────────────────────────────────────────────────────

    private const int TeamSize = 3;

    private readonly List<FighterData>[] _picks = { new(), new() };
    private int _firstPickTeamIndex;
    private int _pickNumber;

    public int  PickNumber         => _pickNumber;
    public int  FirstPickTeamIndex => _firstPickTeamIndex;

    public bool RestrictionsEnabled
    {
        get => restrictionsEnabled;
        set { restrictionsEnabled = value; OnRestrictionsChanged?.Invoke(); ui?.RefreshAll(); }
    }

    public int  ActiveTeamIndex => GetTeamIndexForPick(_pickNumber);
    public bool DraftComplete   => _pickNumber >= TeamSize * 2;

    // ── Events ─────────────────────────────────────────────────────────────

    public static event System.Action<int, FighterData> OnPickMade;
    public static event System.Action<int>              OnPickTurnChanged;
    public static event System.Action                   OnDraftComplete;
    public static event System.Action                   OnRestrictionsChanged;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (!IsNetworked)
            InitHotseat();
    }

    public void InitHotseat()
    {
        LocalTeamIndex = -1;
        MatchSetup.LocalTeamId = 0;
        GenerateMapWithSeed(Random.Range(1, int.MaxValue));
        StartDraftInternal();
    }

    // ── Map ────────────────────────────────────────────────────────────────

    public void GenerateMapWithSeed(int seed)
    {
        Debug.Log($"[CSM] GenerateMapWithSeed({seed}) — board={board != null} terrain={terrainGenerator != null} renderer={boardRenderer != null} camera={cameraController != null}");
        board.Initialize();
        terrainGenerator.Initialize(board);
        terrainGenerator.SetSeed(seed);
        terrainGenerator.Generate();
        boardRenderer.Initialize(board);
        cameraController.FitToBoard(board);
        MatchSetup.MapSeed = seed;
    }

    // ── Draft ──────────────────────────────────────────────────────────────

    public void StartDraftInternal()
    {
        _pickNumber         = 0;
        _firstPickTeamIndex = Random.Range(0, 2);
        _picks[0].Clear();
        _picks[1].Clear();
        MatchSetup.FirstActingTeam = _firstPickTeamIndex == 0 ? 2 : 1;
        OnPickTurnChanged?.Invoke(ActiveTeamIndex);
        ui?.RefreshAll();
    }

    public void TryPick(FighterData fighter)
    {
        if (IsNetworked && !IsServer)
            CharSelectNetworkBridge.Instance?.CmdTryPick(fighter.name);
        else
            ProcessPick(fighter.name);
    }

    public void ProcessPick(string fighterName)
    {
        if (DraftComplete) return;

        var roster = FighterLoader.LoadRoster();
        if (!roster.TryGetValue(fighterName, out var fighter)) return;
        if (IsAlreadyPicked(fighter)) return;

        int teamIdx = ActiveTeamIndex;

        if (restrictionsEnabled && !IsPickAllowed(teamIdx, fighter))
        {
            Debug.Log($"[CharSelect] {fighter.name} blocked by restriction rules.");
            return;
        }

        _picks[teamIdx].Add(fighter);
        _pickNumber++;

        if (DraftComplete)
            CommitToMatchSetup();

        OnPickMade?.Invoke(teamIdx, fighter);

        if (DraftComplete)
            OnDraftComplete?.Invoke();
        else
            OnPickTurnChanged?.Invoke(ActiveTeamIndex);

        ui?.RefreshAll();
    }

    // Called on clients to apply a pick broadcast from the server
    public void ApplyPickFromNetwork(int teamIdx, string fighterName, int newPickNumber,
                                     bool draftComplete, string[] t1, string[] t2, int firstActing)
    {
        var roster = FighterLoader.LoadRoster();
        if (!roster.TryGetValue(fighterName, out var fighter)) return;

        _picks[teamIdx].Add(fighter);
        _pickNumber = newPickNumber;

        OnPickMade?.Invoke(teamIdx, fighter);

        if (draftComplete)
        {
            MatchSetup.Team1Fighters   = t1;
            MatchSetup.Team2Fighters   = t2;
            MatchSetup.FirstActingTeam = firstActing;
            OnDraftComplete?.Invoke();
        }
        else
        {
            OnPickTurnChanged?.Invoke(ActiveTeamIndex);
        }

        ui?.RefreshAll();
    }

    public void ResetDraft()
    {
        if (IsNetworked && !IsServer)
            CharSelectNetworkBridge.Instance?.CmdResetDraft();
        else
            ProcessReset();
    }

    public void ProcessReset()
    {
        _pickNumber         = 0;
        _firstPickTeamIndex = Random.Range(0, 2);
        _picks[0].Clear();
        _picks[1].Clear();
        MatchSetup.FirstActingTeam = _firstPickTeamIndex == 0 ? 2 : 1;
        OnPickTurnChanged?.Invoke(ActiveTeamIndex);
        ui?.RefreshAll();
    }

    // Called on clients to apply a reset broadcast from the server
    public void ApplyResetFromNetwork(int firstPickTeamIndex)
    {
        _pickNumber         = 0;
        _firstPickTeamIndex = firstPickTeamIndex;
        _picks[0].Clear();
        _picks[1].Clear();
        MatchSetup.FirstActingTeam = firstPickTeamIndex == 0 ? 2 : 1;
        OnPickTurnChanged?.Invoke(ActiveTeamIndex);
        ui?.RefreshAll();
    }

    // ── Queries ────────────────────────────────────────────────────────────

    public bool IsAlreadyPicked(FighterData fighter)
        => _picks[0].Any(f => f.name == fighter.name) ||
           _picks[1].Any(f => f.name == fighter.name);

    public bool IsPickAllowed(int teamIndex, FighterData fighter)
    {
        var currentRarities = _picks[teamIndex].Select(f => f.rarity).ToList();
        currentRarities.Add(fighter.rarity);
        if (currentRarities.Count == TeamSize)
            return RestrictionEngine.IsValidTeam(currentRarities);
        var allowedRanks = RestrictionEngine.AllowedNextRanks(
            _picks[teamIndex].Select(f => f.rarity).ToList());
        return allowedRanks.Contains(RestrictionEngine.GetRank(fighter.rarity));
    }

    public HashSet<int> GetAllowedRanksForActiveTeam()
    {
        if (!restrictionsEnabled) return new HashSet<int> { 0, 1, 2, 3, 4 };
        return RestrictionEngine.AllowedNextRanks(
            _picks[ActiveTeamIndex].Select(f => f.rarity).ToList());
    }

    public IReadOnlyList<FighterData> GetPicks(int teamIndex) => _picks[teamIndex];

    // ── Scene transition ───────────────────────────────────────────────────

    public void StartBattle()
    {
        if (!MatchSetup.IsReady)
        {
            Debug.LogWarning("[CharSelect] Draft not complete.");
            return;
        }

        if (IsNetworked)
            CharSelectNetworkBridge.Instance?.ServerStartBattle();
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene(battleSceneName);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private int GetTeamIndexForPick(int pickNum)
        => (_firstPickTeamIndex + pickNum) % 2;

    private void CommitToMatchSetup()
    {
        MatchSetup.Team1Fighters = _picks[0].Select(f => f.name).ToArray();
        MatchSetup.Team2Fighters = _picks[1].Select(f => f.name).ToArray();
    }
}
