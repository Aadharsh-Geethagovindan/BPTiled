using UnityEngine;
using UnityEngine.UI;

// One team's pick strip in the bottom bar. Place two of these in CharSelectUI — one per team.
public class TeamPicksPanel : MonoBehaviour
{
    [SerializeField] private Image[] portraits;   // 3 slots, pre-made in the prefab
    [SerializeField] private Image[] rarityPips;  // 3 pips, one per slot (colored by rarity)

    [Header("Rarity Pip Colors")]
    [SerializeField] private Color colorC  = new Color(0.85f, 0.85f, 0.90f);
    [SerializeField] private Color colorUC = new Color(0.60f, 0.90f, 0.65f);
    [SerializeField] private Color colorR  = new Color(0.40f, 0.60f, 0.95f);
    [SerializeField] private Color colorUR = new Color(0.70f, 0.40f, 0.95f);
    [SerializeField] private Color colorL  = new Color(1.00f, 0.82f, 0.20f);

    private int _teamIndex; // 0-based

    public void Initialize(int teamIndex)
    {
        _teamIndex = teamIndex;
        ClearAll();
    }

    public void Refresh()
    {
        ClearAll();

        var picks = CharSelectManager.Instance.GetPicks(_teamIndex);
        for (int i = 0; i < picks.Count && i < portraits.Length; i++)
        {
            var fighter = picks[i];

            if (portraits[i] != null)
            {
                portraits[i].sprite         = FighterLoader.LoadSprite(fighter.imageName);
                portraits[i].preserveAspect = true;
                portraits[i].color          = Color.white;
            }

            if (rarityPips[i] != null)
            {
                rarityPips[i].color   = RarityColor(fighter.rarity);
                rarityPips[i].enabled = true;
            }
        }
    }

    private void ClearAll()
    {
        for (int i = 0; i < portraits.Length; i++)
        {
            if (portraits[i]  != null) { portraits[i].sprite  = null; portraits[i].color = new Color(1,1,1,0.2f); }
            if (rarityPips[i] != null) rarityPips[i].enabled = false;
        }
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
