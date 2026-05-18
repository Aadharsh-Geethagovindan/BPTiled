using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One character card in the selection grid.
// Prefab needs: root Image (background), TextMeshProUGUI nameText, Image portrait, Button button.
public class SelectionCard : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image           portrait;
    [SerializeField] private Button          button;
    [SerializeField] private Image           background;

    [Header("Rarity Colors")]
    [SerializeField] private Color colorC  = new Color(0.85f, 0.85f, 0.90f); // silver-white
    [SerializeField] private Color colorUC = new Color(0.60f, 0.90f, 0.65f); // pale green
    [SerializeField] private Color colorR  = new Color(0.40f, 0.60f, 0.95f); // blue
    [SerializeField] private Color colorUR = new Color(0.70f, 0.40f, 0.95f); // purple
    [SerializeField] private Color colorL  = new Color(1.00f, 0.82f, 0.20f); // gold

    [Header("State Colors")]
    [SerializeField] private Color pickedTint  = new Color(0.4f, 0.4f, 0.4f, 1f); // greyed out
    [SerializeField] private Color blockedTint = new Color(0.5f, 0.5f, 0.5f, 0.6f);
    [SerializeField] private Color normalTint  = Color.white;

    private FighterData _data;
    private CharSelectUI _ui;

    public void Setup(FighterData data, CharSelectUI ui)
    {
        _data = data;
        _ui   = ui;

        nameText.text  = data.name;
        nameText.color = RarityColor(data.rarity);

        var sprite = FighterLoader.LoadSprite(data.imageName);
        if (portrait != null)
        {
            portrait.sprite         = sprite;
            portrait.preserveAspect = true;
        }

        button.onClick.AddListener(OnClicked);
    }

    public void Refresh()
    {
        if (_data == null) return;

        bool picked  = CharSelectManager.Instance.IsAlreadyPicked(_data);
        bool blocked = !picked
            && CharSelectManager.Instance.RestrictionsEnabled
            && !CharSelectManager.Instance.IsPickAllowed(
                CharSelectManager.Instance.ActiveTeamIndex, _data);
        bool draftDone = CharSelectManager.Instance.DraftComplete;

        button.interactable = !picked && !blocked && !draftDone;

        Color tint = picked  ? pickedTint
                   : blocked ? blockedTint
                   :           normalTint;

        if (background != null) background.color = tint;
        if (portrait   != null) portrait.color   = tint;
    }

    private void OnClicked()
    {
        _ui?.SetPending(_data);
    }

    private Color RarityColor(string rarity) => rarity switch
    {
        "L"  => colorL,
        "UR" => colorUR,
        "R"  => colorR,
        "UC" => colorUC,
        _    => colorC,
    };
}
