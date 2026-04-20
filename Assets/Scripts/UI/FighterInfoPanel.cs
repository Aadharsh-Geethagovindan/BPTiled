using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FighterInfoPanel : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image           portrait;

    [Header("Sliders")]
    [SerializeField] private Slider          hpSlider;
    [SerializeField] private TextMeshProUGUI hpValueText;
    [SerializeField] private Slider          chargeSlider;
    [SerializeField] private TextMeshProUGUI chargeValueText;

    [Header("Movement")]
    [SerializeField] private TextMeshProUGUI moveText;
    [SerializeField] private Button          moveButton;

    [Header("Stats")]
    [SerializeField] private TextMeshProUGUI crValue;
    [SerializeField] private TextMeshProUGUI cdValue;
    [SerializeField] private TextMeshProUGUI accValue;
    [SerializeField] private TextMeshProUGUI dodValue;
    [SerializeField] private TextMeshProUGUI dmgMultValue;

    [Header("Resistance Grid")]
    [SerializeField] private TextMeshProUGUI resArcaneVal;
    [SerializeField] private TextMeshProUGUI resElementalVal;
    [SerializeField] private TextMeshProUGUI resForceVal;
    [SerializeField] private TextMeshProUGUI resCorruptVal;

    private Fighter _displayedFighter;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        SelectionManager.OnFighterSelected   += Show;
        SelectionManager.OnFighterDeselected += Hide;
        SelectionManager.OnMovePointsChanged += UpdateMoveText;
        SelectionManager.OnMoveModeChanged   += UpdateMoveButton;
        Fighter.OnHPChanged                  += HandleHPChanged;
        Fighter.OnChargeChanged              += HandleChargeChanged;
        Fighter.OnStatusEffectsChanged       += HandleStatusEffectsChanged;
        Fighter.OnFighterDied                += HandleFighterDied;

        if (moveButton != null)
            moveButton.onClick.AddListener(() => SelectionManager.Instance.EnterMoveMode());
    }

    private void OnDestroy()
    {
        SelectionManager.OnFighterSelected   -= Show;
        SelectionManager.OnFighterDeselected -= Hide;
        SelectionManager.OnMovePointsChanged -= UpdateMoveText;
        SelectionManager.OnMoveModeChanged   -= UpdateMoveButton;
        Fighter.OnHPChanged                  -= HandleHPChanged;
        Fighter.OnChargeChanged              -= HandleChargeChanged;
        Fighter.OnStatusEffectsChanged       -= HandleStatusEffectsChanged;
        Fighter.OnFighterDied                -= HandleFighterDied;
    }

    // ── Show / Hide ────────────────────────────────────────────────────────

    private void Show(Fighter fighter)
    {
        _displayedFighter     = fighter;
        nameText.text         = fighter.FighterName;
        hpSlider.interactable = false;
        hpSlider.minValue     = 0;
        hpSlider.maxValue     = fighter.MaxHP;
        SetHP(fighter.CurrentHP, fighter.MaxHP);

        if (chargeSlider != null)
        {
            chargeSlider.interactable = false;
            chargeSlider.minValue     = 0;
            chargeSlider.maxValue     = fighter.SigChargeReq;
            SetCharge(fighter.CurrentCharge, fighter.SigChargeReq);
        }

        if (portrait != null && fighter.Portrait != null)
        {
            portrait.sprite         = fighter.Portrait;
            portrait.preserveAspect = true;
        }

        UpdateMoveText(fighter.RemainingMovePoints, fighter.Speed);
        if (moveButton != null) moveButton.interactable = fighter.RemainingMovePoints > 0f;

        PopulateStats(fighter);
        PopulateResistances(fighter);
    }

    private void Hide()
    {
        _displayedFighter = null;
        nameText.text     = string.Empty;
        SetHP(0, 0);
        SetCharge(0, 0);
        if (portrait   != null) portrait.sprite = null;
        if (moveText   != null) moveText.text   = string.Empty;
        if (moveButton != null) moveButton.interactable = false;

        ClearStats();
        ClearResistances();
    }

    // ── Stats ──────────────────────────────────────────────────────────────

    private void PopulateStats(Fighter f)
    {
        SetText(crValue,      Pct(f.GetModifiedCritRate()));
        SetText(cdValue,      Pct(f.GetModifiedCritDmg()));
        SetText(accValue,     Pct(f.GetModifiedAccuracy()));
        SetText(dodValue,     Pct(f.GetModifiedDodge()));
        SetText(dmgMultValue, $"{f.GetModifiedDamageMultiplier():0.##}x");
    }

    private void ClearStats()
    {
        SetText(crValue,      string.Empty);
        SetText(cdValue,      string.Empty);
        SetText(accValue,     string.Empty);
        SetText(dodValue,     string.Empty);
        SetText(dmgMultValue, string.Empty);
    }

    // ── Resistances ────────────────────────────────────────────────────────

    private void PopulateResistances(Fighter f)
    {
        SetText(resArcaneVal,    Pct(f.GetModifiedResistance(AbilityEssence.Arcane)));
        SetText(resElementalVal, Pct(f.GetModifiedResistance(AbilityEssence.Elemental)));
        SetText(resForceVal,     Pct(f.GetModifiedResistance(AbilityEssence.Force)));
        SetText(resCorruptVal,   Pct(f.GetModifiedResistance(AbilityEssence.Corrupt)));
    }

    private void ClearResistances()
    {
        SetText(resArcaneVal,    string.Empty);
        SetText(resElementalVal, string.Empty);
        SetText(resForceVal,     string.Empty);
        SetText(resCorruptVal,   string.Empty);
    }

    // ── HP / Charge setters ────────────────────────────────────────────────

    private void SetHP(float current, float max)
    {
        hpSlider.value = current;
        SetText(hpValueText, max > 0 ? $"{Mathf.RoundToInt(current)}/{Mathf.RoundToInt(max)}" : string.Empty);
    }

    private void SetCharge(float current, float max)
    {
        if (chargeSlider != null) chargeSlider.value = current;
        SetText(chargeValueText, max > 0 ? $"{Mathf.RoundToInt(current)}/{Mathf.RoundToInt(max)}" : string.Empty);
    }

    // ── HP / Move event handlers ───────────────────────────────────────────

    private void HandleHPChanged(Fighter fighter)
    {
        if (fighter != _displayedFighter) return;
        SetHP(fighter.CurrentHP, fighter.MaxHP);
    }

    private void HandleChargeChanged(Fighter fighter)
    {
        if (fighter != _displayedFighter) return;
        SetCharge(fighter.CurrentCharge, fighter.SigChargeReq);
    }

    private void UpdateMoveText(float remaining, float max)
    {
        if (moveText != null)
            moveText.text = $"{Mathf.RoundToInt(remaining)} / {Mathf.RoundToInt(max)}";

        if (moveButton != null)
            moveButton.interactable = remaining > 0f;
    }

    private void HandleStatusEffectsChanged(Fighter fighter)
    {
        if (fighter != _displayedFighter) return;
        PopulateStats(fighter);
        PopulateResistances(fighter);
    }

    private void HandleFighterDied(Fighter fighter)
    {
        if (fighter != _displayedFighter) return;
        Hide();
    }

    private void UpdateMoveButton(bool inMoveMode) { }

    // ── Utilities ──────────────────────────────────────────────────────────

    // Converts a float to a whole-number percentage string, e.g. 0.1 → "10%", 1.5 → "150%"
    private static string Pct(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

    private static void SetText(TextMeshProUGUI label, string text)
    {
        if (label != null) label.text = text;
    }
}
