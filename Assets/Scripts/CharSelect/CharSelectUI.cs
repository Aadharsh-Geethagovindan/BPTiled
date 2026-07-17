using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharSelectUI : MonoBehaviour
{
    [SerializeField] private CharacterGridPanel    gridPanel;
    [SerializeField] private CharacterPreviewPanel previewPanel;
    [SerializeField] private TeamPicksPanel        team1PicksPanel;
    [SerializeField] private TeamPicksPanel        team2PicksPanel;

    [Header("Buttons")]
    [SerializeField] private Button          confirmButton;
    [SerializeField] private TextMeshProUGUI confirmButtonText;
    [SerializeField] private Image           confirmButtonAccent;
    [SerializeField] private Button          clearButton;
    [SerializeField] private Button          showHideButton;
    [SerializeField] private TextMeshProUGUI showHideButtonText;

    [Header("Panels to toggle")]
    [SerializeField] private GameObject[] toggledPanels;

    private static readonly Color RoyalBlue      = new Color(0.25f,  0.41f,  0.88f);
    private static readonly Color AccentDefault  = new Color(1.00f,  0.882f, 0.00f); // FFE100

    private FighterData _pendingPick;
    private bool        _draftComplete;
    private Coroutine   _blinkCoroutine;

    private void Awake()
    {
        team1PicksPanel?.Initialize(0);
        team2PicksPanel?.Initialize(1);

        confirmButton.interactable = false;
        confirmButton.onClick.AddListener(OnConfirmClicked);
        clearButton.onClick.AddListener(OnClearClicked);
        showHideButton.onClick.AddListener(TogglePanels);

        CharSelectManager.OnPickMade            += HandlePickMade;
        CharSelectManager.OnPickTurnChanged     += HandlePickTurnChanged;
        CharSelectManager.OnDraftComplete       += HandleDraftComplete;
        CharSelectManager.OnRestrictionsChanged += RefreshAll;
    }

    private void OnDestroy()
    {
        CharSelectManager.OnPickMade            -= HandlePickMade;
        CharSelectManager.OnPickTurnChanged     -= HandlePickTurnChanged;
        CharSelectManager.OnDraftComplete       -= HandleDraftComplete;
        CharSelectManager.OnRestrictionsChanged -= RefreshAll;
    }

    public void SetPending(FighterData fighter)
    {
        if (_draftComplete) return;
        _pendingPick = fighter;
        RefreshConfirmState();
        previewPanel?.Show(fighter);
    }

    private void OnConfirmClicked()
    {
        if (_draftComplete)
        {
            CharSelectManager.Instance.StartBattle();
            return;
        }

        if (_pendingPick == null) return;
        CharSelectManager.Instance.TryPick(_pendingPick);
        _pendingPick = null;
        if (!_draftComplete)
            confirmButton.interactable = false;
    }

    private void OnClearClicked()
    {
        CharSelectManager.Instance.ResetDraft();
        _pendingPick   = null;
        _draftComplete = false;

        confirmButton.interactable = false;
        if (confirmButtonText  != null) confirmButtonText.text = "Confirm";
        StopBlink();
    }

    private void TogglePanels()
    {
        bool anyActive = toggledPanels.Length > 0 && toggledPanels[0].activeSelf;
        foreach (var panel in toggledPanels)
            panel.SetActive(!anyActive);
        showHideButtonText.text = anyActive ? "Show" : "Hide";
    }

    public void RefreshAll()
    {
        gridPanel?.Refresh();
        team1PicksPanel?.Refresh();
        team2PicksPanel?.Refresh();
    }

    private void HandlePickMade(int teamIndex, FighterData fighter)
    {
        _pendingPick = null;
        team1PicksPanel?.Refresh();
        team2PicksPanel?.Refresh();
        gridPanel?.Refresh();
        RefreshConfirmState();
    }

    private void HandlePickTurnChanged(int teamIndex)
    {
        gridPanel?.Refresh();
        team1PicksPanel?.Refresh();
        team2PicksPanel?.Refresh();
        RefreshConfirmState();
    }

    private void RefreshConfirmState()
    {
        if (_draftComplete) return;
        int local    = CharSelectManager.Instance.LocalTeamIndex;
        int active   = CharSelectManager.Instance.ActiveTeamIndex;
        bool isMyTurn = local == -1 || local == active;
        confirmButton.interactable = _pendingPick != null && isMyTurn;
    }

    private void HandleDraftComplete()
    {
        gridPanel?.Refresh();
        _draftComplete             = true;
        _pendingPick               = null;
        confirmButton.interactable = true;
        if (confirmButtonText != null) confirmButtonText.text = "Start";
        StartBlink();
    }

    private void StartBlink()
    {
        StopBlink();
        if (confirmButtonAccent != null)
            _blinkCoroutine = StartCoroutine(BlinkAccent());
    }

    private void StopBlink()
    {
        if (_blinkCoroutine != null)
        {
            StopCoroutine(_blinkCoroutine);
            _blinkCoroutine = null;
        }

        if (confirmButtonAccent != null)
            confirmButtonAccent.color = AccentDefault;
    }

    private IEnumerator BlinkAccent()
    {
        while (true)
        {
            float alpha = Mathf.Lerp(0.15f, 1f, (Mathf.Sin(Time.time * 3f) + 1f) * 0.5f);
            confirmButtonAccent.color = new Color(RoyalBlue.r, RoyalBlue.g, RoyalBlue.b, alpha);
            yield return null;
        }
    }
}
