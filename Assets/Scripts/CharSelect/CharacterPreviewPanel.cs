using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharacterPreviewPanel : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private Image           portrait;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI rarityText;

    [Header("Core Stats")]
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI speedText;
    [SerializeField] private TextMeshProUGUI sigChargeText;
    [SerializeField] private TextMeshProUGUI accuracyText;
    [SerializeField] private TextMeshProUGUI critRateText;
    [SerializeField] private TextMeshProUGUI critDmgText;

    [Header("Resistances")]
    [SerializeField] private TextMeshProUGUI resArcaneText;
    [SerializeField] private TextMeshProUGUI resElementalText;
    [SerializeField] private TextMeshProUGUI resForceText;
    [SerializeField] private TextMeshProUGUI resCorruptText;

    [Header("Ability Display")]
    [SerializeField] private Image           abilityIcon;
    [SerializeField] private TextMeshProUGUI abilityNameText;
    [SerializeField] private TextMeshProUGUI abilityDescText;
    [SerializeField] private TextMeshProUGUI abilityCooldownText;

    [Header("Ability Slot Buttons")]
    [SerializeField] private Button passiveButton;
    [SerializeField] private Button normalButton;
    [SerializeField] private Button skillButton;
    [SerializeField] private Button skill2Button;
    [SerializeField] private Button sigButton;

    private FighterData _currentFighter;

    private void Awake()
    {
        passiveButton.onClick.AddListener(() => ShowSlot("Passive"));
        normalButton.onClick.AddListener(()  => ShowSlot("Normal"));
        skillButton.onClick.AddListener(()   => ShowSlot("Skill"));
        skill2Button.onClick.AddListener(()  => ShowSlot("Skill2"));
        sigButton.onClick.AddListener(()     => ShowSlot("Signature"));

        gameObject.SetActive(false);
    }

    public void Show(FighterData fighter)
    {
        _currentFighter = fighter;
        gameObject.SetActive(true);
        Populate(fighter);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void Populate(FighterData f)
    {
        if (portrait != null)
        {
            portrait.sprite         = FighterLoader.LoadSprite(f.imageName);
            portrait.preserveAspect = true;
        }

        if (nameText   != null) nameText.text  = f.name;
        if (rarityText != null) rarityText.text = f.rarity;

        if (hpText        != null) hpText.text       = $"HP: {f.hp}";
        if (speedText     != null) speedText.text     = $"SPD: {f.speed.ToString("0.#")}";
        if (sigChargeText != null) sigChargeText.text = $"S.C.R.: {f.sigChargeReq}";
        if (accuracyText  != null) accuracyText.text  = $"Acc: {Mathf.RoundToInt(f.accuracy * 100)}%";
        if (critRateText  != null) critRateText.text  = $"Crit Rate: {Mathf.RoundToInt(f.critRate * 100)}%";
        if (critDmgText   != null) critDmgText.text   = $"Cr. Dmg: {Mathf.RoundToInt(f.critDmg * 100)}%";

        var res = f.resistances;
        if (resArcaneText    != null) resArcaneText.text    = FormatRes(res?.arcane    ?? 0);
        if (resElementalText != null) resElementalText.text = FormatRes(res?.elemental ?? 0);
        if (resForceText     != null) resForceText.text     = FormatRes(res?.force     ?? 0);
        if (resCorruptText   != null) resCorruptText.text   = FormatRes(res?.corrupt   ?? 0);

        RefreshSlotButtons(f);

        var defaultMove = GetMove(f, "Normal") ?? GetMove(f, "Passive");
        if (defaultMove != null) DisplayMove(defaultMove);
        else ClearAbilityDisplay();
    }

    private void ShowSlot(string slot)
    {
        if (_currentFighter == null) return;
        FighterMoveData move;
        if (slot == "Skill2")
        {
            var skills = GetAllMoves(_currentFighter, "Skill");
            move = skills.Count > 1 ? skills[1] : null;
        }
        else
        {
            move = GetMove(_currentFighter, slot);
        }
        if (move != null) DisplayMove(move);
    }

    private void DisplayMove(FighterMoveData move)
    {
        if (abilityNameText != null) abilityNameText.text = move.name;
        if (abilityDescText != null) abilityDescText.text = move.mechanics ?? string.Empty;

        if (abilityIcon != null)
        {
            var sprite = Resources.Load<Sprite>($"effecticons/{move.essence.ToLower()}");
            abilityIcon.sprite  = sprite;
            abilityIcon.enabled = sprite != null;
        }

        if (abilityCooldownText != null)
        {
            abilityCooldownText.text = move.cooldown > 0 ? $"CD {move.cooldown}" : string.Empty;
        }
    }

    private void ClearAbilityDisplay()
    {
        if (abilityNameText     != null) abilityNameText.text     = string.Empty;
        if (abilityDescText     != null) abilityDescText.text     = string.Empty;
        if (abilityCooldownText != null) abilityCooldownText.text = string.Empty;
        if (abilityIcon         != null) abilityIcon.enabled      = false;
    }

    private void RefreshSlotButtons(FighterData f)
    {
        passiveButton.interactable = GetMove(f, "Passive") != null;
        normalButton.interactable  = GetMove(f, "Normal")  != null;
        sigButton.interactable     = GetMove(f, "Signature") != null;

        // In JSON both skills are stored as "Skill"; second one is Skill2 at runtime
        var skills = GetAllMoves(f, "Skill");
        skillButton.interactable  = skills.Count > 0;
        skill2Button.interactable = skills.Count > 1;
    }

    private static FighterMoveData GetMove(FighterData f, string slot)
    {
        if (f.moves == null) return null;
        foreach (var m in f.moves)
            if (m.type == slot) return m;
        return null;
    }

    private static System.Collections.Generic.List<FighterMoveData> GetAllMoves(FighterData f, string slot)
    {
        var result = new System.Collections.Generic.List<FighterMoveData>();
        if (f.moves == null) return result;
        foreach (var m in f.moves)
            if (m.type == slot) result.Add(m);
        return result;
    }

    private static string FormatRes(float val)
    {
        if (val == 0f) return "0%";
        return (val > 0 ? "+" : "") + Mathf.RoundToInt(val * 100) + "%";
    }
}
