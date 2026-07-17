using TMPro;
using UnityEngine;

// Handles the two persistent text labels at the top of the battle screen:
// - promptText: "Your Turn" / "Waiting for opponent..." / "Team X's Turn" (hotseat)
// - playerLabel: "Player 1" / "Player 2" — debug label, set once and never changes
public class BattleHUD : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private TextMeshProUGUI playerLabel;

    private void Awake()
    {
        TurnManager.OnActivationChanged += HandleActivationChanged;
    }

    private void OnDestroy()
    {
        TurnManager.OnActivationChanged -= HandleActivationChanged;
    }

    private void Start()
    {
        if (playerLabel != null)
        {
            playerLabel.text = MatchSetup.LocalTeamId switch
            {
                1 => "Player 1",
                2 => "Player 2",
                _ => string.Empty
            };
        }
    }

    private void HandleActivationChanged(int activeTeamId)
    {
        if (promptText == null) return;

        if (MatchSetup.LocalTeamId == 0)
            promptText.text = $"Team {activeTeamId}'s Turn";
        else
            promptText.text = activeTeamId == MatchSetup.LocalTeamId
                ? "Your Turn"
                : "Waiting for opponent...";
    }
}
