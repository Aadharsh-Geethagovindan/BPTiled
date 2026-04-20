using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class TurnTrackerPanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI    roundText;
    [SerializeField] private Transform          team1Container;  // HorizontalLayoutGroup
    [SerializeField] private Transform          team2Container;  // HorizontalLayoutGroup
    [SerializeField] private FighterPortraitCard cardPrefab;

    private readonly Dictionary<Fighter, FighterPortraitCard> _cards = new();
    private TurnManager _turnManager;

    // Called from BattleController after fighters are spawned.
    public void Initialize(FighterManager fighterManager, TurnManager turnManager)
    {
        _turnManager   = turnManager;
        roundText.text = $"Round {turnManager.RoundNumber}";

        foreach (var fighter in fighterManager.AllFighters)
        {
            var container = fighter.TeamId == 1 ? team1Container : team2Container;
            var card      = Instantiate(cardPrefab, container);
            card.Setup(fighter);
            _cards[fighter] = card;
        }

        RefreshAllStates();

        TurnManager.OnRoundStarted      += HandleRoundStarted;
        TurnManager.OnFighterActivated  += HandleFighterActivated;
        TurnManager.OnFighterTurnEnded  += HandleFighterTurnEnded;
        TurnManager.OnActivationChanged += HandleActivationChanged;
        Fighter.OnHPChanged             += HandleHPChanged;
    }

    private void OnDestroy()
    {
        TurnManager.OnRoundStarted      -= HandleRoundStarted;
        TurnManager.OnFighterActivated  -= HandleFighterActivated;
        TurnManager.OnFighterTurnEnded  -= HandleFighterTurnEnded;
        TurnManager.OnActivationChanged -= HandleActivationChanged;
        Fighter.OnHPChanged             -= HandleHPChanged;
    }

    // ── Event handlers ─────────────────────────────────────────────────────

    private void HandleRoundStarted(int round)
    {
        roundText.text = $"Round {round}";
        RefreshAllStates();
    }

    private void HandleFighterActivated(Fighter fighter)  => RefreshCard(fighter);
    private void HandleFighterTurnEnded(Fighter fighter)  => RefreshCard(fighter);
    private void HandleActivationChanged(int teamId)      => RefreshAllStates();

    private void HandleHPChanged(Fighter fighter)
    {
        if (!_cards.TryGetValue(fighter, out var card)) return;
        card.UpdateHP(fighter.CurrentHP);
        if (fighter.IsDead) RefreshCard(fighter);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void RefreshCard(Fighter fighter)
    {
        if (!_cards.TryGetValue(fighter, out var card)) return;
        card.UpdateState(fighter, fighter == _turnManager.ActiveFighter);
    }

    private void RefreshAllStates()
    {
        foreach (var kvp in _cards)
            kvp.Value.UpdateState(kvp.Key, kvp.Key == _turnManager.ActiveFighter);
    }
}
