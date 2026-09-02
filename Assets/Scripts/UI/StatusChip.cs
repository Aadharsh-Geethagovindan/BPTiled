using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StatusChip : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI durationText;
    [SerializeField] private Image           iconImage;

    [SerializeField] private Color buffColor   = Color.green;
    [SerializeField] private Color debuffColor = Color.red;

    [Header("Stack Pips")]
    // Empty RectTransform (was the old "Stacks" text object) — pips are generated as children of
    // this, left-to-right, on every Setup() call. No LayoutGroup needed/assumed; positioning is
    // done in code here, same as every other child on this chip (hand-placed, not auto-flowed).
    [SerializeField] private RectTransform pipContainer;
    [SerializeField] private Sprite        pipSprite;
    [SerializeField] private float         pipSize    = 8f;
    [SerializeField] private float         pipSpacing = 4f;

    private const int MaxPips = 5; // stacks beyond this just stop adding pips — no overflow "+N" yet

    private readonly List<GameObject> _pips = new List<GameObject>();

    public void Setup(StatusEffect effect)
    {
        nameText.text      = effect.Name;
        nameText.color     = effect.IsDebuff ? debuffColor : buffColor;
        durationText.text  = effect.Duration > 0 ? $"{effect.Duration}" : string.Empty;
        iconImage.sprite  = LoadIcon(effect);

        UpdatePips(effect);
    }

    // Pips: hidden at 1 stack (same rule the old "x2" text used), one pip per stack up to
    // MaxPips, tinted the same buff/debuff color as the name text.
    private void UpdatePips(StatusEffect effect)
    {
        ClearPips();

        if (effect.Stacks <= 1 || pipContainer == null || pipSprite == null) return;

        int   count = Mathf.Min(effect.Stacks, MaxPips);
        Color tint  = effect.IsDebuff ? debuffColor : buffColor;

        for (int i = 0; i < count; i++)
        {
            var pipObj = new GameObject($"Pip{i}", typeof(RectTransform), typeof(Image));
            pipObj.transform.SetParent(pipContainer, false);

            var rt = (RectTransform)pipObj.transform;
            rt.anchorMin        = new Vector2(0f, 0.5f);
            rt.anchorMax        = new Vector2(0f, 0.5f);
            rt.pivot             = new Vector2(0f, 0.5f);
            rt.sizeDelta         = new Vector2(pipSize, pipSize);
            rt.anchoredPosition  = new Vector2(i * (pipSize + pipSpacing), 0f);

            var img = pipObj.GetComponent<Image>();
            img.sprite = pipSprite;
            img.color  = tint;

            _pips.Add(pipObj);
        }
    }

    private void ClearPips()
    {
        foreach (var pip in _pips)
            if (pip != null) Destroy(pip);
        _pips.Clear();
    }

    private static Sprite LoadIcon(StatusEffect effect)
    {
        string path = ResolveIconPath(effect);
        var sprite  = Resources.Load<Sprite>("effecticons/" + path);
        if (sprite == null)
            sprite = Resources.Load<Sprite>("effecticons/normalIcon");
        return sprite;
    }

    private static string ResolveIconPath(StatusEffect effect)
    {
        bool buff = !effect.IsDebuff;

        return effect.Type switch
        {
            StatusEffectType.DamageOverTime     => DotIcon(effect.Essence),
            StatusEffectType.HealOverTime       => "hot",
            StatusEffectType.AccuracyModifier   => buff ? "accBuff"   : "accDebuff",
            StatusEffectType.DodgeModifier      => buff ? "dodgeBuff" : "dodgeDebuff",
            StatusEffectType.CritRateModifier   => "crRate",
            StatusEffectType.CritDamageModifier => "crDmg",
            StatusEffectType.DamageMultiplier   => buff ? "dmgBuff"   : "dmgDebuff",
            StatusEffectType.ResistanceModifier => buff ? "resBuff"   : "resDebuff",
            StatusEffectType.SpeedModifier      => buff ? "speedBuff" : "spdDebuff",
            StatusEffectType.Root               => "spdDebuff",
            _                                   => "normalIcon"
        };
    }

    private static string DotIcon(string essence) => essence switch
    {
        "Arcane"    => "arcane",
        "Elemental" => "elemental",
        "Force"     => "force",
        "Corrupt"   => "corrupt",
        _           => "poison"   // True / None / unknown
    };
}
