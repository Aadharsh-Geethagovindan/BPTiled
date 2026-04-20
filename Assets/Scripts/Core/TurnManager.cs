using System;
using System.Collections.Generic;
using UnityEngine;

// [SERVER] TurnManager owns all round and turn logic.
// Client code reads public state (ActiveTeamId, ActiveFighter, RoundNumber)
// and subscribes to events for UI updates. Never call state-mutating methods
// from client code directly — route through BattleController request methods.
public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }

    public int     RoundNumber   { get; private set; }
    public int     ActiveTeamId  { get; private set; } = 1;
    public Fighter ActiveFighter { get; private set; }

    // ── Events (UI subscribes to these) ────────────────────────────────────
    public static event Action<int>     OnRoundStarted;       // round number
    public static event Action<int>     OnActivationChanged;  // team id whose pick it is
    public static event Action<Fighter> OnFighterActivated;
    public static event Action<Fighter> OnFighterTurnEnded;
    public static event Action<int>     OnRoundEnded;         // round number
    public static event Action<int>     OnGameOver;           // winning team id

    private FighterManager _fighterManager;
    private bool           _gameOver;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Initialize(FighterManager fighterManager)
    {
        _fighterManager = fighterManager;
    }

    // ── Round control ──────────────────────────────────────────────────────

    public void StartRound()
    {
        RoundNumber++;
        ActiveFighter = null;

        foreach (var fighter in _fighterManager.AllFighters)
            fighter.ResetRoundState();

        // Round 1: respect draft result. Subsequent rounds alternate from last round's starter.
        ActiveTeamId = RoundNumber == 1 ? MatchSetup.FirstActingTeam : (ActiveTeamId == 1 ? 2 : 1);

        Debug.Log($"[TurnManager] Round {RoundNumber} started. Team {ActiveTeamId} picks first.");
        OnRoundStarted?.Invoke(RoundNumber);
        OnActivationChanged?.Invoke(ActiveTeamId);
    }

    // ── Fighter activation ─────────────────────────────────────────────────

    /// Returns true if this fighter can be activated for a turn right now.
    public bool CanActivate(Fighter fighter)
    {
        return ActiveFighter == null
            && fighter.TeamId == ActiveTeamId
            && !fighter.HasActivatedThisRound
            && !fighter.IsDead;
    }

    public void ActivateFighter(Fighter fighter)
    {
        if (!CanActivate(fighter)) return;

        ActiveFighter = fighter;
        fighter.ResetTurnState();

        // Tick cooldowns and periodic effects (DoT/HoT) at turn start
        foreach (var ability in fighter.Abilities)
            ability.TickCooldown();
        fighter.TickPeriodicEffects();

        // DoT may have killed the fighter — skip the turn entirely if so
        if (fighter.IsDead)
        {
            ActiveFighter = fighter; // set briefly so EndFighterTurn finds it
            EndFighterTurn();
            return;
        }

        Debug.Log($"[TurnManager] {fighter.FighterName} (Team {fighter.TeamId}) activated.");
        OnFighterActivated?.Invoke(fighter);
    }

    /// Called when player cancels before moving or acting — returns pick to active team.
    public void CancelActivation()
    {
        if (ActiveFighter == null) return;
        if (ActiveFighter.HasMovedThisTurn || ActiveFighter.HasActedThisTurn) return;

        Debug.Log($"[TurnManager] {ActiveFighter.FighterName} activation cancelled.");
        ActiveFighter = null;
        // ActiveTeamId stays the same — same team picks again
        OnActivationChanged?.Invoke(ActiveTeamId);
    }

    // ── Turn end ───────────────────────────────────────────────────────────

    public void EndFighterTurn()
    {
        if (ActiveFighter == null || _gameOver) return;

        // Tick duration on stat-modifying effects at turn end
        ActiveFighter.TickDurationEffects();

        ActiveFighter.SetActivated(true);
        var ended = ActiveFighter;
        ActiveFighter = null;

        Debug.Log($"[TurnManager] {ended.FighterName}'s turn ended.");
        OnFighterTurnEnded?.Invoke(ended);

        if (CheckGameOver()) return;

        if (IsRoundOver())
        {
            EndRound();
        }
        else
        {
            // Alternate to the other team; if they have no eligible fighters, stay on current team
            int nextTeam = ActiveTeamId == 1 ? 2 : 1;
            ActiveTeamId = HasEligibleFighters(nextTeam) ? nextTeam : ActiveTeamId;

            Debug.Log($"[TurnManager] Team {ActiveTeamId} picks next.");
            OnActivationChanged?.Invoke(ActiveTeamId);
        }
    }

    // ── Round end ──────────────────────────────────────────────────────────

    private void EndRound()
    {
        Debug.Log($"[TurnManager] Round {RoundNumber} ended.");
        OnRoundEnded?.Invoke(RoundNumber);
        StartRound();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private bool CheckGameOver()
    {
        bool team1Alive = false, team2Alive = false;
        foreach (var f in _fighterManager.AllFighters)
        {
            if (f.IsDead) continue;
            if (f.TeamId == 1) team1Alive = true;
            else               team2Alive = true;
        }

        if (team1Alive && team2Alive) return false;

        _gameOver    = true;
        int winner   = team1Alive ? 1 : 2;
        Debug.Log($"[TurnManager] Game over — Team {winner} wins!");
        OnGameOver?.Invoke(winner);
        return true;
    }

    private bool IsRoundOver()
    {
        foreach (var fighter in _fighterManager.AllFighters)
            if (!fighter.IsDead && !fighter.HasActivatedThisRound)
                return false;
        return true;
    }

    private bool HasEligibleFighters(int teamId)
    {
        foreach (var fighter in _fighterManager.AllFighters)
            if (fighter.TeamId == teamId && !fighter.IsDead && !fighter.HasActivatedThisRound)
                return true;
        return false;
    }
}
