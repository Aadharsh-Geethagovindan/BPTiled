using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owns all character select state: draft order, picks, restrictions, and scene transition.
public class CharSelectManager : MonoBehaviour
{
    public static CharSelectManager Instance { get; private set; }

    [Header("Scene refs")]
    [SerializeField] private Board             board;
    [SerializeField] private TerrainGenerator  terrainGenerator;
    [SerializeField] private BoardRenderer     boardRenderer;
    [SerializeField] private CameraController  cameraController;

    [Header("Settings")]
    [SerializeField] private string battleSceneName = "Battle";
    [SerializeField] private bool   restrictionsEnabled = true;

    [Header("UI")]
    [SerializeField] private CharSelectUI ui;

    // ── Draft state ────────────────────────────────────────────────────────

    private const int TeamSize = 3;

    // Index 0 = Team 1, Index 1 = Team 2
    private readonly List<FighterData>[] _picks = { new(), new() };

    // Pick order: 0 = Team 1 picks first, 1 = Team 2 picks first (randomized)
    private int _firstPickTeamIndex;   // 0-based
    private int _pickNumber;           // 0..5 (6 total picks)

    public bool RestrictionsEnabled
    {
        get => restrictionsEnabled;
        set { restrictionsEnabled = value; OnRestrictionsToggled(); }
    }

    public int ActiveTeamIndex   => GetTeamIndexForPick(_pickNumber);
    public bool DraftComplete    => _pickNumber >= TeamSize * 2;

    // ── Events ─────────────────────────────────────────────────────────────
    public static event System.Action<int, FighterData> OnPickMade;     // teamIndex, fighter
    public static event System.Action<int>              OnPickTurnChanged; // teamIndex whose turn it is
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
        GenerateMap();
        StartDraft();
    }

    // ── Map ────────────────────────────────────────────────────────────────

    private void GenerateMap()
    {
        board.Initialize();
        terrainGenerator.Initialize(board);
        terrainGenerator.Generate();
        boardRenderer.Initialize(board);
        cameraController.FitToBoard(board);

        MatchSetup.MapSeed = terrainGenerator.LastSeed;
    }

    // ── Draft ──────────────────────────────────────────────────────────────

    private void StartDraft()
    {
        _pickNumber        = 0;
        _firstPickTeamIndex = Random.Range(0, 2);
        _picks[0].Clear();
        _picks[1].Clear();

        // Team that picks SECOND acts FIRST in battle
        MatchSetup.FirstActingTeam = (_firstPickTeamIndex == 0) ? 2 : 1;

        OnPickTurnChanged?.Invoke(ActiveTeamIndex);
        ui?.RefreshAll();
    }

    /// Called when a player clicks a character card.
    public void TryPick(FighterData fighter)
    {
        if (DraftComplete) return;
        if (IsAlreadyPicked(fighter)) return;

        int teamIdx = ActiveTeamIndex;

        if (restrictionsEnabled && !IsPickAllowed(teamIdx, fighter))
        {
            Debug.Log($"[CharSelect] {fighter.name} blocked by restriction rules.");
            return;
        }

        _picks[teamIdx].Add(fighter);
        OnPickMade?.Invoke(teamIdx, fighter);

        _pickNumber++;

        if (DraftComplete)
        {
            CommitToMatchSetup();
            OnDraftComplete?.Invoke();
        }
        else
        {
            OnPickTurnChanged?.Invoke(ActiveTeamIndex);
        }

        ui?.RefreshAll();
    }

    /// Remove the last pick for a team (undo).
    public void UndoLastPick(int teamIndex)
    {
        if (_picks[teamIndex].Count == 0) return;

        // Only allow undo if it was this team's most recent pick
        int lastPickTeam = GetTeamIndexForPick(_pickNumber - 1);
        if (lastPickTeam != teamIndex) return;

        _picks[teamIndex].RemoveAt(_picks[teamIndex].Count - 1);
        _pickNumber--;

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

        // Check if any pattern still works after adding this pick
        currentRarities.Add(fighter.rarity);
        if (currentRarities.Count == TeamSize)
            return RestrictionEngine.IsValidTeam(currentRarities);

        // Mid-draft: check that at least one valid completion still exists
        var allowedRanks = RestrictionEngine.AllowedNextRanks(
            _picks[teamIndex].Select(f => f.rarity).ToList());
        return allowedRanks.Contains(RestrictionEngine.GetRank(fighter.rarity));
    }

    /// Returns the set of rarity ranks the active team can still legally pick.
    /// Used by UI to grey out cards.
    public HashSet<int> GetAllowedRanksForActiveTeam()
    {
        if (!restrictionsEnabled)
            return new HashSet<int> { 0, 1, 2, 3, 4 };

        return RestrictionEngine.AllowedNextRanks(
            _picks[ActiveTeamIndex].Select(f => f.rarity).ToList());
    }

    public IReadOnlyList<FighterData> GetPicks(int teamIndex) => _picks[teamIndex];

    // ── Scene transition ───────────────────────────────────────────────────

    public void StartBattle()
    {
        if (!MatchSetup.IsReady)
        {
            Debug.LogWarning("[CharSelect] Draft not complete — cannot start battle.");
            return;
        }
        SceneManager.LoadScene(battleSceneName);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    // Alternating pick order starting from _firstPickTeamIndex
    private int GetTeamIndexForPick(int pickNum)
        => (_firstPickTeamIndex + pickNum) % 2;

    private void CommitToMatchSetup()
    {
        MatchSetup.Team1Fighters = _picks[0].Select(f => f.name).ToArray();
        MatchSetup.Team2Fighters = _picks[1].Select(f => f.name).ToArray();
    }

    private void OnRestrictionsToggled()
    {
        OnRestrictionsChanged?.Invoke();
        ui?.RefreshAll();
    }
}
