using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Populates the victory screen on scene load: winner text + the winning team's portraits,
// spawned into the TeamPanel's HorizontalLayoutGroup. Portraits are built entirely in code — no
// prefab needed. Just attach this, wire the two references, and it populates itself in Start().
public class VictoryScreen : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI victorText;
    [SerializeField] private Transform       teamPanel;      // the HorizontalLayoutGroup container
    [SerializeField] private Vector2         portraitSize = new Vector2(160f, 160f);

    private void Start()
    {
        int winningTeam = MatchSetup.WinningTeamId;

        if (victorText != null)
            victorText.text = $"Team {winningTeam} Wins!";

        if (teamPanel == null) return;

        // Clear any placeholder children left over from designing the layout in the Editor.
        for (int i = teamPanel.childCount - 1; i >= 0; i--)
            Destroy(teamPanel.GetChild(i).gameObject);

        string[] fighterNames = winningTeam == 1 ? MatchSetup.Team1Fighters : MatchSetup.Team2Fighters;
        if (fighterNames == null) return;

        var roster = FighterLoader.LoadRoster();

        foreach (var name in fighterNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (!roster.TryGetValue(name, out var data)) continue;
            SpawnPortrait(data);
        }
    }

    private void SpawnPortrait(FighterData data)
    {
        var obj = new GameObject($"Portrait_{data.name}", typeof(RectTransform));
        obj.transform.SetParent(teamPanel, false);

        // LayoutElement takes priority over the HorizontalLayoutGroup's own child-sizing rules,
        // so this size holds regardless of how "Control Child Size" is configured on TeamPanel.
        var layoutElement = obj.AddComponent<LayoutElement>();
        layoutElement.preferredWidth  = portraitSize.x;
        layoutElement.preferredHeight = portraitSize.y;

        var image = obj.AddComponent<Image>();
        image.sprite         = FighterLoader.LoadSprite(data.imageName);
        image.preserveAspect = true;
    }
}
