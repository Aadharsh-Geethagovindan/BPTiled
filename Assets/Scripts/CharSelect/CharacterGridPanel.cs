using System.Collections.Generic;
using UnityEngine;

// Scrollable grid of all selectable characters.
// Attach to the left panel root. Needs a ScrollRect child with a GridLayoutGroup content transform.
public class CharacterGridPanel : MonoBehaviour
{
    [SerializeField] private SelectionCard cardPrefab;
    [SerializeField] private Transform     content;       // GridLayoutGroup parent
    [SerializeField] private CharSelectUI  ui;

    private readonly List<SelectionCard> _cards = new();

    private void Start()
    {
        BuildGrid();
    }

    private void BuildGrid()
    {
        var roster = FighterLoader.LoadRoster();

        foreach (var kvp in roster)
        {
            var card = Instantiate(cardPrefab, content);
            card.Setup(kvp.Value, ui);
            _cards.Add(card);
        }
    }

    public void Refresh()
    {
        foreach (var card in _cards)
            card.Refresh();
    }
}
