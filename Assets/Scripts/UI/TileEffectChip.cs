using TMPro;
using UnityEngine;

// Chip displayed in TileInfoPanel for each active TileEffect on the selected tile.
public class TileEffectChip : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI durationText;
    [SerializeField] private TextMeshProUGUI detailsText;

    [SerializeField] private Color allyColor  = new Color(0.4f, 0.8f, 0.4f);
    [SerializeField] private Color enemyColor = new Color(0.9f, 0.4f, 0.4f);
    [SerializeField] private Color neutralColor = Color.white;

    public void Setup(TileEffect effect)
    {
        if (nameText != null)
        {
            nameText.text  = effect.Name;
            nameText.color = effect.Affinity switch
            {
                TileEffectAffinity.AllyOnly  => allyColor,
                TileEffectAffinity.EnemyOnly => enemyColor,
                _                            => neutralColor,
            };
        }

        if (durationText != null)
            durationText.text = effect.Duration > 0 ? $"{effect.Duration}t" : string.Empty;

        if (detailsText != null)
            detailsText.text = BuildDetails(effect);
    }

    private static string BuildDetails(TileEffect effect)
    {
        var parts = new System.Collections.Generic.List<string>();

        if (effect.Damage   > 0) parts.Add($"-{effect.Damage} HP");
        if (effect.Healing  > 0) parts.Add($"+{effect.Healing} HP");
        if (effect.Shielding > 0) parts.Add($"+{effect.Shielding} Shield");

        foreach (var se in effect.StatusEffectsToApply)
            parts.Add(se.Name);

        return string.Join("  ", parts);
    }
}
