using System.Collections.Generic;
using UnityEngine;

// Owns all passive ability logic.
// Subscribes to generic Fighter events — no passive logic lives in Fighter.cs.
// All fighter-name checks are isolated here.
public class PassiveManager : MonoBehaviour
{
    public static PassiveManager Instance { get; private set; }

    private FighterManager _fighterManager;
    private Board          _board;

    // ── Battle-Scarred tracker ─────────────────────────────────────────────
    // Tracks how many 100-HP increments of resistance have already been applied per fighter.
    private readonly Dictionary<Fighter, int> _battleScarredStacks = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Initialize(FighterManager fighterManager, Board board)
    {
        _fighterManager = fighterManager;
        _board          = board;

        // Passive logic mutates Fighter state directly and is not synced via RPC — it must run
        // exactly once per action, on whichever peer is authoritative. In online mode a pure
        // client would otherwise re-run this same logic locally every time it mirrors a server
        // event (e.g. NetworkApplyActivation), producing duplicate log lines and independently
        // rolled RNG that can diverge from the server's result.
        if (MatchSetup.Mode == GameMode.Online && !FishNet.InstanceFinder.IsServerStarted)
            return;

        Fighter.OnFighterDamaged       += OnFighterDamaged;
        Fighter.OnFighterDied          += OnFighterDied;
        Fighter.OnStatusEffectApplied  += OnStatusEffectApplied;
        Fighter.OnFighterMoved         += OnFighterMoved;
        TurnManager.OnFighterActivated += OnTurnStart;
    }

    private void OnDestroy()
    {
        Fighter.OnFighterDamaged       -= OnFighterDamaged;
        Fighter.OnFighterDied          -= OnFighterDied;
        Fighter.OnStatusEffectApplied  -= OnStatusEffectApplied;
        Fighter.OnFighterMoved         -= OnFighterMoved;
        TurnManager.OnFighterActivated -= OnTurnStart;
    }

    // ── Hooks ──────────────────────────────────────────────────────────────

    // Called when a fighter's turn starts.
    private void OnTurnStart(Fighter fighter)
    {
        // Deadeye — Rellin: for every 10% accuracy over 100%, apply one stack of Sniper Focus
        if (fighter.FighterName == "Rellin")
        {
            float excess = fighter.GetModifiedAccuracy() - 1f;
            int stacks   = Mathf.FloorToInt(excess / 0.1f);
            if (stacks > 0)
            {
                var effect = new StatusEffect("Sniper Focus", StatusEffectType.DamageMultiplier,
                                              "None", 0.1f, 1, false);
                for (int i = 0; i < stacks; i++)
                    fighter.ApplyStatusEffect(effect);

                BattleLogger.Log($"Deadeye: {fighter.FighterName} gained {stacks}x Sniper Focus.", LogCategory.Passive);
            }
        }

        // Rally — Dinso: all allies within 4 tiles gain Inspired (20% damage multiplier) for 1 turn
        if (fighter.FighterName == "Captain Dinso")
        {
            foreach (var ally in _fighterManager.AllFighters)
            {
                if (ally == fighter || ally.TeamId != fighter.TeamId || ally.IsDead) continue;
                if (ManhattanDistance(fighter.GridPosition, ally.GridPosition) <= 4)
                {
                    ally.ApplyStatusEffect(
                        new StatusEffect("Inspired", StatusEffectType.DamageMultiplier, "None", 0.2f, 1, false));
                    BattleLogger.Log($"Rally: {ally.FighterName} gained Inspired.", LogCategory.Passive);
                }
            }
        }

        // Ward Resonance — Vas Drel: 30% chance to grant Boost to a random adjacent ally if within 2 tiles of any ally
        if (fighter.FighterName == "Vas Drel")
        {
            bool nearAlly = false;
            var adjacentAllies = new List<Fighter>();

            foreach (var ally in _fighterManager.AllFighters)
            {
                if (ally == fighter || ally.TeamId != fighter.TeamId || ally.IsDead) continue;
                int dist = ManhattanDistance(fighter.GridPosition, ally.GridPosition);
                if (dist <= 2) nearAlly = true;
                if (dist == 1) adjacentAllies.Add(ally);
            }

            if (nearAlly && adjacentAllies.Count > 0 && Random.value < 0.3f)
            {
                var chosen = adjacentAllies[Random.Range(0, adjacentAllies.Count)];
                chosen.ApplyStatusEffect(
                    new StatusEffect("Boost", StatusEffectType.CritDamageModifier, "None", 0.45f, 2, false));
                BattleLogger.Log($"Ward Resonance: {chosen.FighterName} gained Boost.", LogCategory.Passive);
            }
        }
    }

    // Called when a fighter takes HP damage.
    private void OnFighterDamaged(Fighter fighter, int hpDamage)
    {
        // Battle-Scarred — Krakoa: +10% Corrupt and Force resist per 100 HP lost
        if (fighter.FighterName == "Krakoa")
        {
            int hpLost    = fighter.MaxHP - fighter.CurrentHP;
            int newStacks = hpLost / 100;

            if (!_battleScarredStacks.ContainsKey(fighter))
                _battleScarredStacks[fighter] = 0;

            int diff = newStacks - _battleScarredStacks[fighter];
            if (diff > 0)
            {
                _battleScarredStacks[fighter] = newStacks;
                fighter.ModifyResistance(AbilityEssence.Corrupt, diff * 0.1f);
                fighter.ModifyResistance(AbilityEssence.Force,   diff * 0.1f);
                BattleLogger.Log($"Battle-Scarred: {fighter.FighterName} gained +{diff * 10}% Corrupt/Force resist.", LogCategory.Passive);
            }
        }

        // Reactive Dodge — Jack: +6% dodge on taking damage, capped at 24%
        if (fighter.FighterName == "Jack")
        {
            float current = fighter.DodgeChance;
            if (current < 0.24f)
            {
                float add = Mathf.Min(0.06f, 0.24f - current);
                fighter.ModifyDodge(add);
                BattleLogger.Log($"Reactive Dodge: {fighter.FighterName}'s dodge is now {fighter.DodgeChance:P0}.", LogCategory.Passive);
            }
        }
    }

    // Called when a fighter dies.
    private void OnFighterDied(Fighter fighter)
    {
        // Clean up Battle-Scarred tracker
        _battleScarredStacks.Remove(fighter);

        // Martyr's Bloom — Avarice: on death, place Vitalized on a 2x3 zone centered on her tile
        if (fighter.FighterName == "Avarice" && TileEffectManager.Instance != null)
        {
            var vitalized = new AbilityTileEffect
            {
                Name    = "Vitalized",
                Trigger = TileEffectTrigger.OnTurnEnd,
                Affinity = TileEffectAffinity.AllyOnly,
                Healing  = 20,
                Duration = 10,
            };

            // 2x3 box: 2 wide (dx -1..0), 3 tall (dy -1..1), centered on Avarice's position
            var origin = fighter.GridPosition;
            for (int dx = -1; dx <= 0; dx++)
            for (int dy = -1; dy <= 1; dy++)
                TileEffectManager.Instance.PlaceEffect(origin + new Vector2Int(dx, dy), vitalized, fighter.TeamId);

            BattleLogger.Log($"Martyr's Bloom: Vitalized zone placed at {origin}.", LogCategory.Passive);
        }
    }

    // Called when any status effect is applied to a fighter.
    private void OnStatusEffectApplied(Fighter caster, Fighter target, StatusEffect effect)
    {
        // Hemorrhage — Sanguine: 15% chance to apply a DoT a second time when Sanguine applies one
        if (caster != null && caster.FighterName == "Sanguine"
            && effect.Type == StatusEffectType.DamageOverTime
            && Random.value < 0.15f)
        {
            var bonus = new StatusEffect(effect.Name, effect.Type, effect.Essence,
                                         effect.Magnitude, effect.Duration, effect.IsDebuff);
            target.ApplyStatusEffect(bonus); // caster intentionally null to avoid infinite recursion
            BattleLogger.Log($"Hemorrhage: {effect.Name} applied a second time to {target.FighterName}.", LogCategory.Passive);
        }
    }

    // Called when a fighter moves.
    private void OnFighterMoved(Fighter fighter, int tilesMoved)
    {
        // Leyline Flow — Arkhe: +2 speed for 1 turn per 10 tiles moved (cumulative)
        if (fighter.FighterName == "Arkhe")
        {
            int thresholds = fighter.TotalTilesMoved / 10;
            int previous   = (fighter.TotalTilesMoved - tilesMoved) / 10;
            int newTriggers = thresholds - previous;

            for (int i = 0; i < newTriggers; i++)
            {
                fighter.ApplyStatusEffect(
                    new StatusEffect("Flighted", StatusEffectType.SpeedModifier, "None", 2f, 1, false));
                BattleLogger.Log($"Leyline Flow: {fighter.FighterName} gained Flighted.", LogCategory.Passive);
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static int ManhattanDistance(Vector2Int a, Vector2Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
}
