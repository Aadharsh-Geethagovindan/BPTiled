using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StatusChip : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI durationText;
    [SerializeField] private TextMeshProUGUI stacksText;
    [SerializeField] private Image           iconImage;

    [SerializeField] private Color buffColor   = Color.green;
    [SerializeField] private Color debuffColor = Color.red;

    public void Setup(StatusEffect effect)
    {
        nameText.text      = effect.Name;
        nameText.color     = effect.IsDebuff ? debuffColor : buffColor;
        durationText.text  = effect.Duration > 0 ? $"{effect.Duration}" : string.Empty;
        iconImage.sprite  = LoadIcon(effect);

        // Stacks text: hidden at 1, visible at 2+
        stacksText.gameObject.SetActive(effect.Stacks > 1);
        if (effect.Stacks > 1)
            stacksText.text = $"x{effect.Stacks}";
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
