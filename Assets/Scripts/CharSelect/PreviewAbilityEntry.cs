using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One row in the ability list inside CharacterPreviewPanel.
// Prefab needs: nameText, typeText, essenceText, descText, and optionally an essence icon Image.
public class PreviewAbilityEntry : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI typeText;
    [SerializeField] private TextMeshProUGUI essenceText;
    [SerializeField] private TextMeshProUGUI descText;
    [SerializeField] private Image           essenceIcon;

    public void Setup(FighterMoveData move)
    {
        if (nameText    != null) nameText.text    = move.name;
        if (typeText    != null) typeText.text    = move.type;
        if (essenceText != null) essenceText.text = move.essence;
        if (descText    != null) descText.text    = move.mechanics;

        if (essenceIcon != null)
        {
            var sprite = Resources.Load<Sprite>($"effecticons/{move.essence.ToLower()}");
            if (sprite != null)
            {
                essenceIcon.sprite  = sprite;
                essenceIcon.enabled = true;
            }
            else
            {
                essenceIcon.enabled = false;
            }
        }
    }
}
