using UnityEngine;
using UnityEngine.UI;

// One team's pick strip in the bottom bar. Place two of these in CharSelectUI — one per team.
public class TeamPicksPanel : MonoBehaviour
{
    // 3 slots each — index-aligned (frames[i] is the parent, fighterPortraits[i] is its "Fighter"
    // child). Rarity pips are gone; the frame itself is now the rarity indicator.
    [SerializeField] private Image[] frames;            // the pick parent's own Image (background frame)
    [SerializeField] private Image[] fighterPortraits;  // the "Fighter" child Image — inactive until a pick lands here

    [Header("Rarity Frame Colors")]
    [SerializeField] private Color colorC  = new Color(0.85f, 0.85f, 0.90f);
    [SerializeField] private Color colorUC = new Color(0.60f, 0.90f, 0.65f);
    [SerializeField] private Color colorR  = new Color(0.40f, 0.60f, 0.95f);
    [SerializeField] private Color colorUR = new Color(0.70f, 0.40f, 0.95f);
    [SerializeField] private Color colorL  = new Color(1.00f, 0.82f, 0.20f);

    private int _teamIndex; // 0-based

    // Each frame's own color as set up in the Editor, captured once at Initialize() rather than
    // hardcoded here — that's the "empty" look ClearAll() restores to, so the placeholder frame
    // color stays whatever's tuned on the prefab/scene instance without needing a script change.
    private Color[] _defaultFrameColors;

    public void Initialize(int teamIndex)
    {
        _teamIndex = teamIndex;

        _defaultFrameColors = new Color[frames.Length];
        for (int i = 0; i < frames.Length; i++)
            if (frames[i] != null)
                _defaultFrameColors[i] = frames[i].color;

        ClearAll();
    }

    public void Refresh()
    {
        ClearAll();

        var picks = CharSelectManager.Instance.GetPicks(_teamIndex);
        for (int i = 0; i < picks.Count && i < fighterPortraits.Length; i++)
        {
            var fighter = picks[i];

            if (fighterPortraits[i] != null)
            {
                fighterPortraits[i].sprite         = FighterLoader.LoadSprite(fighter.imageName);
                fighterPortraits[i].preserveAspect = true;
                fighterPortraits[i].color          = Color.white;
                fighterPortraits[i].gameObject.SetActive(true);
            }

            if (frames[i] != null)
                frames[i].color = RarityColor(fighter.rarity);
        }
    }

    private void ClearAll()
    {
        for (int i = 0; i < fighterPortraits.Length; i++)
        {
            if (fighterPortraits[i] != null)
            {
                fighterPortraits[i].sprite = null;
                fighterPortraits[i].gameObject.SetActive(false);
            }

            if (frames[i] != null && _defaultFrameColors != null)
                frames[i].color = _defaultFrameColors[i];
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
