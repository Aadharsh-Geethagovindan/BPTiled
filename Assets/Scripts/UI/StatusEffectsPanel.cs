using System.Collections.Generic;
using UnityEngine;

public class StatusEffectsPanel : MonoBehaviour
{
    [SerializeField] private Transform   container;   // VerticalLayoutGroup
    [SerializeField] private StatusChip  chipPrefab;

    private Fighter                       _displayedFighter;
    private readonly List<StatusChip>     _chips = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        SelectionManager.OnFighterSelected   += OnFighterSelected;
        SelectionManager.OnFighterDeselected += OnFighterDeselected;
        Fighter.OnStatusEffectsChanged       += OnStatusEffectsChanged;
    }

    private void OnDestroy()
    {
        SelectionManager.OnFighterSelected   -= OnFighterSelected;
        SelectionManager.OnFighterDeselected -= OnFighterDeselected;
        Fighter.OnStatusEffectsChanged       -= OnStatusEffectsChanged;
    }

    // ── Event handlers ─────────────────────────────────────────────────────

    private void OnFighterSelected(Fighter fighter)
    {
        _displayedFighter = fighter;
        Rebuild();
    }

    private void OnFighterDeselected()
    {
        _displayedFighter = null;
        Clear();
    }

    private void OnStatusEffectsChanged(Fighter fighter)
    {
        if (fighter != _displayedFighter) return;
        Rebuild();
    }

    // ── Chip management ────────────────────────────────────────────────────

    private void Rebuild()
    {
        Clear();
        if (_displayedFighter == null) return;

        foreach (var effect in _displayedFighter.StatusEffects)
        {
            if (!effect.ToDisplay) continue;
            var chip = Instantiate(chipPrefab, container);
            chip.Setup(effect);
            _chips.Add(chip);
        }
    }

    private void Clear()
    {
        foreach (var chip in _chips)
            if (chip != null) Destroy(chip.gameObject);
        _chips.Clear();
    }
}
