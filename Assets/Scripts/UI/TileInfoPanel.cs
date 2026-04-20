using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Always-on bottom panel. Shows tile effects when player clicks an empty tile.
// Clears when a fighter is selected.
public class TileInfoPanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI  coordsText;
    [SerializeField] private Transform        effectsContainer; // VerticalLayoutGroup
    [SerializeField] private TileEffectChip   chipPrefab;

    private Vector2Int             _displayedPos;
    private readonly List<TileEffectChip> _chips = new();

    private void Awake()
    {
        SelectionManager.OnTileSelected   += Show;
        SelectionManager.OnTileDeselected += Hide;
        SelectionManager.OnFighterDeselected += Hide;
        TileEffectManager.OnTileEffectsChanged += OnTileEffectsChanged;
    }

    private void OnDestroy()
    {
        SelectionManager.OnTileSelected      -= Show;
        SelectionManager.OnTileDeselected    -= Hide;
        SelectionManager.OnFighterDeselected -= Hide;
        TileEffectManager.OnTileEffectsChanged -= OnTileEffectsChanged;
    }

    private void Show(Vector2Int pos)
    {
        _displayedPos = pos;
        Rebuild();
    }

    private void Hide()
    {
        ClearChips();
        if (coordsText != null) coordsText.text = string.Empty;
    }

    private void OnTileEffectsChanged(Vector2Int pos)
    {
        if (pos == _displayedPos) Rebuild();
    }

    private void Rebuild()
    {
        ClearChips();

        if (coordsText != null)
            coordsText.text = $"Tile ({_displayedPos.x}, {_displayedPos.y})";

        if (TileEffectManager.Instance == null) return;

        var effects = TileEffectManager.Instance.GetEffectsAt(_displayedPos);
        foreach (var effect in effects)
        {
            var chip = Instantiate(chipPrefab, effectsContainer);
            chip.Setup(effect);
            _chips.Add(chip);
        }
    }

    private void ClearChips()
    {
        foreach (var chip in _chips)
            if (chip != null) Destroy(chip.gameObject);
        _chips.Clear();
    }
}
