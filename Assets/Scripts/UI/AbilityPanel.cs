using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AbilityPanel : MonoBehaviour
{
    [Header("Ability Display")]
    [SerializeField] private Image              abilityIcon;
    [SerializeField] private TextMeshProUGUI    abilityName;
    [SerializeField] private TextMeshProUGUI    abilityDesc;
    [SerializeField] private TextMeshProUGUI    cooldownText;

    [Header("Action Buttons")]
    [SerializeField] private Button useButton;
    [SerializeField] private Button cancelButton;

    [Header("Slot Toggle Buttons")]
    [SerializeField] private Button passiveButton;
    [SerializeField] private Button normalButton;
    [SerializeField] private Button skillButton;
    [SerializeField] private Button skill2Button;
    [SerializeField] private Button sigButton;

    private Fighter _currentFighter;
    private Ability _displayedAbility;
    private bool    _isPreviewOnly;

    private static readonly string IconPath = "effecticons/";

    private static Sprite LoadEssenceIcon(AbilityEssence essence)
    {
        string name = essence switch
        {
            AbilityEssence.Arcane    => "arcane",
            AbilityEssence.Elemental => "elemental",
            AbilityEssence.Force     => "force",
            AbilityEssence.Corrupt   => "corrupt",
            _                        => "passiveIcon"
        };
        return Resources.Load<Sprite>(IconPath + name);
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        SelectionManager.OnFighterPreviewed   += OnFighterPreviewed;
        SelectionManager.OnFighterSelected    += OnFighterSelected;
        SelectionManager.OnFighterDeselected  += OnFighterDeselected;
        SelectionManager.OnAbilitySelected    += OnAbilitySelected;
        SelectionManager.OnTargetingCancelled += OnTargetingCancelled;
        SelectionManager.OnAbilityConfirmed   += OnAbilityConfirmed;

        useButton.onClick.AddListener(OnUseClicked);
        cancelButton.onClick.AddListener(OnCancelClicked);

        passiveButton.onClick.AddListener(() => TryShowSlot(AbilitySlot.Passive));
        normalButton.onClick.AddListener(()  => TryShowSlot(AbilitySlot.Normal));
        skillButton.onClick.AddListener(()   => TryShowSlot(AbilitySlot.Skill));
        skill2Button.onClick.AddListener(()  => TryShowSlot(AbilitySlot.Skill2));
        sigButton.onClick.AddListener(()     => TryShowSlot(AbilitySlot.Sig));

        // Start in a cleared state
        ClearDisplay();
        SetTargetingMode(false);
    }

    private void OnDestroy()
    {
        SelectionManager.OnFighterPreviewed   -= OnFighterPreviewed;
        SelectionManager.OnFighterSelected    -= OnFighterSelected;
        SelectionManager.OnFighterDeselected  -= OnFighterDeselected;
        SelectionManager.OnAbilitySelected    -= OnAbilitySelected;
        SelectionManager.OnTargetingCancelled -= OnTargetingCancelled;
        SelectionManager.OnAbilityConfirmed   -= OnAbilityConfirmed;
    }

    // ── SelectionManager event handlers ────────────────────────────────────

    private void OnFighterPreviewed(Fighter fighter)
    {
        _isPreviewOnly  = true;
        PopulateFighter(fighter);
    }

    private void OnFighterSelected(Fighter fighter)
    {
        _isPreviewOnly  = false;
        PopulateFighter(fighter);
    }

    private void PopulateFighter(Fighter fighter)
    {
        _currentFighter = fighter;
        RefreshSlotButtons();
        SetTargetingMode(false);
        var first = GetAbilityInSlot(AbilitySlot.Normal) ?? GetFirstAbility();
        if (first != null) DisplayAbility(first);
        else ClearDisplay();
    }

    private void OnFighterDeselected()
    {
        _isPreviewOnly    = false;
        _currentFighter   = null;
        _displayedAbility = null;
        ClearDisplay();
        SetAllSlotButtonsOff();
        SetTargetingMode(false);
    }

    private void OnAbilitySelected(Ability ability)    => SetTargetingMode(true);
    private void OnTargetingCancelled()                => SetTargetingMode(false);
    private void OnAbilityConfirmed(Fighter f, Ability a, Vector2Int anchor, List<Vector2Int> shape)
        => SetTargetingMode(false);

    // ── Button handlers ────────────────────────────────────────────────────

    private void OnUseClicked()
    {
        if (_displayedAbility == null) return;
        SelectionManager.Instance.SelectAbility(_displayedAbility);
    }

    private void OnCancelClicked()
    {
        SelectionManager.Instance.CancelTargeting();
    }

    // ── Slot toggle logic ──────────────────────────────────────────────────

    private void TryShowSlot(AbilitySlot slot)
    {
        var ability = GetAbilityInSlot(slot);
        if (ability != null) DisplayAbility(ability);
    }

    private void DisplayAbility(Ability ability)
    {
        _displayedAbility = ability;
        abilityName.text  = ability.Name;
        abilityDesc.text  = ability.Description ?? string.Empty;

        if (abilityIcon != null)
            abilityIcon.sprite = LoadEssenceIcon(ability.Essence);

        if (cooldownText != null)
        {
            if (ability.IsOnCooldown)
                cooldownText.text = $"CD {ability.CurrentCooldown}";
            else if (ability.BaseCooldown > 0)
                cooldownText.text = "Ready";
            else
                cooldownText.text = string.Empty;
        }

        RefreshUseButton();
    }

    // Disable USE if the sig is selected but the fighter doesn't have enough charge.
    private void RefreshUseButton()
    {
        if (_displayedAbility == null || _currentFighter == null || _isPreviewOnly)
        {
            useButton.interactable = false;
            return;
        }

        bool isPassive  = _displayedAbility.Slot == AbilitySlot.Passive;
        bool onCooldown = _displayedAbility.IsOnCooldown;
        bool sigBlocked = _displayedAbility.Slot == AbilitySlot.Sig
                          && _currentFighter.CurrentCharge < _currentFighter.SigChargeReq;

        useButton.interactable = !isPassive && !onCooldown && !sigBlocked;
    }

    private void RefreshSlotButtons()
    {
        // Passive/Sig: always clickable (there's always something to read)
        passiveButton.interactable = true;
        sigButton.interactable     = true;

        // Normal/Skill/Skill2: only clickable if the ability exists
        normalButton.interactable  = GetAbilityInSlot(AbilitySlot.Normal)  != null;
        skillButton.interactable   = GetAbilityInSlot(AbilitySlot.Skill)   != null;
        skill2Button.interactable  = GetAbilityInSlot(AbilitySlot.Skill2)  != null;
    }

    private void SetAllSlotButtonsOff()
    {
        passiveButton.interactable = false;
        normalButton.interactable  = false;
        skillButton.interactable   = false;
        skill2Button.interactable  = false;
        sigButton.interactable     = false;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void SetTargetingMode(bool targeting)
    {
        useButton.interactable    = !targeting;
        cancelButton.interactable = targeting;

        // Re-apply sig-charge block when exiting targeting mode
        if (!targeting) RefreshUseButton();
    }

    private void ClearDisplay()
    {
        _displayedAbility = null;
        if (abilityName  != null) abilityName.text  = string.Empty;
        if (abilityDesc  != null) abilityDesc.text  = string.Empty;
        if (cooldownText != null) cooldownText.text = string.Empty;
        if (abilityIcon  != null) abilityIcon.sprite = null;
        useButton.interactable = false;
    }

    private Ability GetAbilityInSlot(AbilitySlot slot)
    {
        if (_currentFighter == null) return null;
        foreach (var a in _currentFighter.Abilities)
            if (a.Slot == slot) return a;
        return null;
    }

    private Ability GetFirstAbility()
    {
        if (_currentFighter == null || _currentFighter.Abilities.Count == 0) return null;
        return _currentFighter.Abilities[0];
    }
}
