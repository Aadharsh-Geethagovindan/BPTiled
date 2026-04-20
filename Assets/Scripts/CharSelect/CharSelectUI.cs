using UnityEngine;

// Root UI controller for the character select scene.
// Holds references to all sub-panels and delegates refreshes to them.
public class CharSelectUI : MonoBehaviour
{
    [SerializeField] private CharacterGridPanel  gridPanel;
    [SerializeField] private CharacterPreviewPanel previewPanel;
    [SerializeField] private TeamPicksPanel      teamPicksPanel;

    private void Awake()
    {
        CharSelectManager.OnPickMade        += HandlePickMade;
        CharSelectManager.OnPickTurnChanged += HandlePickTurnChanged;
        CharSelectManager.OnDraftComplete   += HandleDraftComplete;
        CharSelectManager.OnRestrictionsChanged += RefreshAll;
    }

    private void OnDestroy()
    {
        CharSelectManager.OnPickMade        -= HandlePickMade;
        CharSelectManager.OnPickTurnChanged -= HandlePickTurnChanged;
        CharSelectManager.OnDraftComplete   -= HandleDraftComplete;
        CharSelectManager.OnRestrictionsChanged -= RefreshAll;
    }

    public void RefreshAll()
    {
        gridPanel?.Refresh();
        teamPicksPanel?.Refresh();
    }

    private void HandlePickMade(int teamIndex, FighterData fighter)
    {
        teamPicksPanel?.Refresh();
        gridPanel?.Refresh();
    }

    private void HandlePickTurnChanged(int teamIndex)
    {
        gridPanel?.Refresh();
        teamPicksPanel?.Refresh();
    }

    private void HandleDraftComplete()
    {
        gridPanel?.Refresh();
    }

    // Called by a character card when clicked
    public void ShowPreview(FighterData fighter)
    {
        previewPanel?.Show(fighter);
    }

    public void HidePreview()
    {
        previewPanel?.Hide();
    }
}
