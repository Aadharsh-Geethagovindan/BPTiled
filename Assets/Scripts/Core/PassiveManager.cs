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

    // ── Judicial Resolve tracker ────────────────────────────────────────────
    // Fighters who have already spent their once-per-game trigger.
    private readonly HashSet<Fighter> _judicialResolveUsed = new();

    // ── Breach Specialist tracker ───────────────────────────────────────────
    // Cumulative count of enemy hits landed, for the every-5-hits Focused stack.
    private readonly Dictionary<Fighter, int> _enemyHitCounts = new();

    // ── Constellian Trooper tracker ─────────────────────────────────────────
    // How many 75-damage increments have already been granted per fighter.
    private readonly Dictionary<Fighter, int> _trooperDamageStacks = new();

    // ── VyGar tracker ────────────────────────────────────────────────────────
    // Whether this fighter has had at least one prior turn (gates "after the first round"), and
    // whether they've taken damage since their last own turn ended.
    private readonly HashSet<Fighter> _vygarHadFirstTurn         = new();
    private readonly HashSet<Fighter> _vygarDamagedSinceLastTurn = new();

    // ── Trex tracker ─────────────────────────────────────────────────────────
    // Fighters who have already spent their once-per-game "last one standing" trigger.
    private readonly HashSet<Fighter> _lastStandUsed = new();

    // ── Huron tracker ────────────────────────────────────────────────────────
    // How many 100-HP-lost increments of crit damage have already been granted per fighter.
    private readonly Dictionary<Fighter, int> _huronCritStacks = new();

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
        TurnManager.OnGameStart        += HandleGameStart;
        AbilityResolver.OnAbilityUsed  += OnAbilityUsed;
        AbilityResolver.OnEnemyHit     += OnEnemyHit;
        AbilityResolver.OnCrit         += OnCrit;
        AbilityResolver.OnBuffRemoved  += OnBuffRemoved;
    }

    private void OnDestroy()
    {
        Fighter.OnFighterDamaged       -= OnFighterDamaged;
        Fighter.OnFighterDied          -= OnFighterDied;
        Fighter.OnStatusEffectApplied  -= OnStatusEffectApplied;
        Fighter.OnFighterMoved         -= OnFighterMoved;
        TurnManager.OnFighterActivated -= OnTurnStart;
        TurnManager.OnGameStart        -= HandleGameStart;
        AbilityResolver.OnAbilityUsed  -= OnAbilityUsed;
        AbilityResolver.OnEnemyHit     -= OnEnemyHit;
        AbilityResolver.OnCrit         -= OnCrit;
        AbilityResolver.OnBuffRemoved  -= OnBuffRemoved;
    }

    // Called once per action that strips one or more buffs off a target, with how many were removed.
    private void OnBuffRemoved(Fighter caster, int count)
    {
        // Sabotaged Advantage — Vemk Parlas: each buff he strips from an enemy has a 50% chance to
        // grant him 15 shield, independently rolled per buff.
        if (caster.FighterName != "Vemk Parlas") return;

        int totalShield = 0;
        for (int i = 0; i < count; i++)
            if (Random.value < 0.5f)
                totalShield += 15;

        if (totalShield > 0)
        {
            int shielded = caster.AddShield(totalShield);
            BattleLogger.Log($"Sabotaged Advantage: {caster.FighterName} gained {shielded} shield.", LogCategory.Passive);
        }
    }

    // Called once per hit that lands as a crit.
    private void OnCrit(Fighter caster)
    {
        // Mizca: gains a stack of Rage every time it crits (no cap — consumed by Piledriver's
        // dynamicValue for bonus damage). Rage has no inherent effect of its own — Magnitude 0
        // is deliberate, it exists purely as a named counter for Stacks to be read off of.
        if (caster.FighterName != "Mizca") return;

        caster.ApplyStatusEffect(new StatusEffect("Rage", StatusEffectType.DamageMultiplier, "None", 0f, 999, false));
        BattleLogger.Log($"{caster.FighterName} gained a stack of Rage.", LogCategory.Passive);
    }

    // Called once per confirmed hit landed on an enemy.
    private void OnEnemyHit(Fighter caster, Fighter target)
    {
        // Breach Specialist: every 5 hits on enemy targets, gain a stack of Focused (15% dmg mult)
        if (caster.FighterName == "Breach Specialist")
        {
            _enemyHitCounts.TryGetValue(caster, out int count);
            count++;
            _enemyHitCounts[caster] = count;

            if (count % 5 == 0)
            {
                caster.ApplyStatusEffect(new StatusEffect("Focused", StatusEffectType.DamageMultiplier, "None", 0.15f, 999, false));
                BattleLogger.Log($"{caster.FighterName} gained a stack of Focused ({count} hits landed).", LogCategory.Passive);
            }
        }

        // Constellian Trooper: permanent +0.2x damage multiplier for every 75 damage dealt.
        // Threshold-diff tracked off the cumulative TotalDamageDealt field, same pattern as
        // Krakoa's Battle-Scarred (100-HP-lost increments).
        if (caster.FighterName == "Constellian Trooper")
        {
            int newStacks = caster.TotalDamageDealt / 75;
            _trooperDamageStacks.TryGetValue(caster, out int oldStacks);
            int diff = newStacks - oldStacks;
            if (diff > 0)
            {
                _trooperDamageStacks[caster] = newStacks;
                caster.ApplyStatusEffect(new StatusEffect("Overloaded", StatusEffectType.DamageMultiplier, "None", 0.2f * diff, 999, false));
                BattleLogger.Log($"{caster.FighterName} gained +{diff * 20}% damage multiplier ({caster.TotalDamageDealt} total damage dealt).", LogCategory.Passive);
            }
        }
    }

    // ── Game start ─────────────────────────────────────────────────────────
    // Fires once, before Round 1 — for passives with no other natural trigger.

    private void HandleGameStart()
    {
        foreach (var fighter in _fighterManager.AllFighters)
        {
            // Nightmare's Grasp — Bessil: gains Tainted (50% Corrupt resistance) for 5 turns
            if (fighter.FighterName == "Bessil")
            {
                fighter.ApplyStatusEffect(
                    new StatusEffect("Tainted", StatusEffectType.ResistanceModifier, "Corrupt", 0.5f, 5, false));
                BattleLogger.Log($"Nightmare's Grasp: {fighter.FighterName} gained Tainted.", LogCategory.Passive);
            }

            // Aetherian Momentum — Sedra: can always move after attacking, and hard/difficult
            // terrain (cost > 1) costs her half as much. Flat terrain (cost <= 1) is untouched —
            // see GetEffectiveTerrainCost.
            if (fighter.FighterName == "Sedra")
            {
                fighter.SetCanMoveAfterAction(true);
                fighter.SetTerrainCostMultiplier(0.5f);
                fighter.SetTerrainCostThreshold(1f);
                BattleLogger.Log($"Aetherian Momentum: {fighter.FighterName} can move after acting and moves easily over hard terrain.", LogCategory.Passive);
            }

            // Salvation — Temple Guard: all allies (including himself) start the match with a
            // 20 point shield. Unlike Bessil/Sedra above, this grants to the whole team, not just
            // the fighter whose name matched — same team-wide OnGameStart pattern, wider scope.
            if (fighter.FighterName == "Temple Guard")
            {
                foreach (var ally in _fighterManager.AllFighters)
                {
                    if (ally.TeamId != fighter.TeamId) continue;
                    ally.AddShield(20);
                }
                BattleLogger.Log($"Salvation: {fighter.FighterName}'s team starts with a shield.", LogCategory.Passive);
            }
        }
    }

    // Called every time any fighter uses any ability (hit or miss).
    private void OnAbilityUsed(Fighter caster, Ability ability)
    {
        // Overdrive Matrix — K.A.S.: every Sig cast permanently increases her crit damage by 30%
        if (caster.FighterName == "K.A.S." && ability.Slot == AbilitySlot.Sig)
        {
            caster.ApplyStatusEffect(
                new StatusEffect("Overdrive", StatusEffectType.CritDamageModifier, "None", 0.3f, 999, false));
            BattleLogger.Log($"Overdrive Matrix: {caster.FighterName}'s crit damage increased.", LogCategory.Passive);
        }

        // Pack Call — Raish: casting the Sig also grants Hunter (15% crit rate) to all allies
        // within 3 tiles, immediately. Handled here rather than as a second JSON effect because it
        // covers a genuinely different area (3-tile radius) than the Sig's own damage (cross size
        // 1) — the engine only computes one shapeTiles set per click, shared by same-shape effects
        // like Legionary's Hold The Line, but these two areas don't match.
        if (caster.FighterName == "Raish" && ability.Name == "Pack Call")
        {
            foreach (var ally in _fighterManager.AllFighters)
            {
                if (ally == caster || ally.TeamId != caster.TeamId || ally.IsDead) continue;
                if (ManhattanDistance(caster.GridPosition, ally.GridPosition) > 3) continue;

                ally.ApplyStatusEffect(new StatusEffect("Hunter", StatusEffectType.CritRateModifier, "None", 0.15f, 2, false));
                BattleLogger.Log($"Pack Call: {ally.FighterName} gained Hunter.", LogCategory.Passive);
            }
        }
    }

    // ── Pre-damage interception ───────────────────────────────────────────
    // Called from Fighter.TakeDamage before shield/HP are touched. Returns true to fully negate
    // the incoming hit. Unlike the other hooks below (which react after the fact), this one can
    // only be a synchronous, side-effect-free query — nothing to unsubscribe/re-fire.

    public bool ShouldPreventDamage(Fighter target, string essence, int amount)
    {
        // Dark Empress — Rei: immune to Arcane damage of 30 or less
        if (target.FighterName == "Rei" && essence == "Arcane" && amount <= 30)
            return true;

        return false;
    }

    // Called from Fighter.ApplyStatusEffect before a status is added. Returns true to block it
    // entirely (no stacking, no events). Same query-hook shape as ShouldPreventDamage above.
    public bool IsImmuneToStatus(Fighter target, StatusEffectType type)
    {
        // Nightmare's Grasp — Bessil: immune to Stun and Rooted. (Stun itself doesn't do anything
        // mechanically yet — see StatusEffectType.Stun — so this half is inert until that lands.)
        if (target.FighterName == "Bessil" && (type == StatusEffectType.Stun || type == StatusEffectType.Root))
            return true;

        return false;
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

        // Targeting System — Rover: allies within 2 tiles gain Sight (20% accuracy) for 1 turn
        if (fighter.FighterName == "Rover")
        {
            foreach (var ally in _fighterManager.AllFighters)
            {
                if (ally == fighter || ally.TeamId != fighter.TeamId || ally.IsDead) continue;
                if (ManhattanDistance(fighter.GridPosition, ally.GridPosition) <= 2)
                {
                    ally.ApplyStatusEffect(new StatusEffect("Sight", StatusEffectType.AccuracyModifier, "None", 0.2f, 1, false));
                    BattleLogger.Log($"Targeting System: {ally.FighterName} gained Sight.", LogCategory.Passive);
                }
            }
        }

        // Pack Tactics — Raish: gains Howl (+15% dmg mult) for 1 turn if within 2 tiles of an ally
        if (fighter.FighterName == "Raish")
        {
            foreach (var ally in _fighterManager.AllFighters)
            {
                if (ally == fighter || ally.TeamId != fighter.TeamId || ally.IsDead) continue;
                if (ManhattanDistance(fighter.GridPosition, ally.GridPosition) <= 2)
                {
                    fighter.ApplyStatusEffect(new StatusEffect("Howl", StatusEffectType.DamageMultiplier, "None", 0.15f, 1, false));
                    BattleLogger.Log($"Pack Tactics: {fighter.FighterName} gained Howl.", LogCategory.Passive);
                    break;
                }
            }
        }

        // Frost Ward — Virae: she and any ally within 1 tile gain a 10 point shield
        if (fighter.FighterName == "Virae")
        {
            fighter.AddShield(10);
            foreach (var ally in _fighterManager.AllFighters)
            {
                if (ally == fighter || ally.TeamId != fighter.TeamId || ally.IsDead) continue;
                if (ManhattanDistance(fighter.GridPosition, ally.GridPosition) <= 1)
                    ally.AddShield(10);
            }
            BattleLogger.Log($"Frost Ward: {fighter.FighterName} and nearby allies gained shield.", LogCategory.Passive);
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

        // IWO's Patronage — Ulmika: removes a random debuff from an ally within 2 tiles if any has
        // one (priority on allies); only targets herself if no such ally qualifies.
        if (fighter.FighterName == "Ulmika")
        {
            var debuffedAllies = new List<Fighter>();
            foreach (var ally in _fighterManager.AllFighters)
            {
                if (ally == fighter || ally.TeamId != fighter.TeamId || ally.IsDead) continue;
                if (ManhattanDistance(fighter.GridPosition, ally.GridPosition) > 2) continue;
                if (HasDebuff(ally)) debuffedAllies.Add(ally);
            }

            Fighter cleanseTarget = debuffedAllies.Count > 0
                ? debuffedAllies[Random.Range(0, debuffedAllies.Count)]
                : (HasDebuff(fighter) ? fighter : null);

            if (cleanseTarget != null)
            {
                var removed = cleanseTarget.RemoveRandomStatusEffect(isDebuff: true);
                if (removed != null)
                    BattleLogger.Log($"IWO's Patronage: {cleanseTarget.FighterName} lost {removed}.", LogCategory.Passive);
            }
        }

        // Scrap Ingenuity — Trustless Engineer: independent 30% chance to cut skill cooldowns by 2
        // and 25% chance to gain 20% sig charge, each turn.
        if (fighter.FighterName == "Trustless Engineer")
        {
            if (Random.value < 0.3f)
            {
                fighter.AddCooldownToSkills(-2);
                BattleLogger.Log($"Scrap Ingenuity: {fighter.FighterName}'s skill cooldowns reduced.", LogCategory.Passive);
            }
            if (Random.value < 0.25f)
            {
                fighter.ModifyChargePercent(0.2f);
                BattleLogger.Log($"Scrap Ingenuity: {fighter.FighterName} gained sig charge.", LogCategory.Passive);
            }
        }

        // VyGar: after his first turn, if he wasn't damaged since his last turn ended, gain Hunter
        // (15% crit rate) for 2 turns. Checked against the watch window BEFORE resetting it for the
        // upcoming turn-to-turn interval.
        if (fighter.FighterName == "VyGar")
        {
            if (_vygarHadFirstTurn.Contains(fighter) && !_vygarDamagedSinceLastTurn.Contains(fighter))
            {
                fighter.ApplyStatusEffect(new StatusEffect("Hunter", StatusEffectType.CritRateModifier, "None", 0.15f, 2, false));
                BattleLogger.Log($"{fighter.FighterName} gained Hunter (undamaged since his last turn).", LogCategory.Passive);
            }
            _vygarHadFirstTurn.Add(fighter);
            _vygarDamagedSinceLastTurn.Remove(fighter);
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

        // Divine Vigor — Huron: +10% crit damage for every 100 HP lost
        if (fighter.FighterName == "Huron")
        {
            int hpLost    = fighter.MaxHP - fighter.CurrentHP;
            int newStacks = hpLost / 100;

            _huronCritStacks.TryGetValue(fighter, out int oldStacks);
            int diff = newStacks - oldStacks;
            if (diff > 0)
            {
                _huronCritStacks[fighter] = newStacks;
                fighter.ApplyStatusEffect(new StatusEffect("Divine Vigor", StatusEffectType.CritDamageModifier, "None", 0.1f * diff, 999, false));
                BattleLogger.Log($"Divine Vigor: {fighter.FighterName} gained +{diff * 10}% crit damage.", LogCategory.Passive);
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

        // VyGar: mark the current watch window as broken — checked/cleared in OnTurnStart.
        if (fighter.FighterName == "VyGar")
            _vygarDamagedSinceLastTurn.Add(fighter);
    }

    // Called when a fighter dies.
    private void OnFighterDied(Fighter fighter)
    {
        // Clean up Battle-Scarred tracker
        _battleScarredStacks.Remove(fighter);
        _enemyHitCounts.Remove(fighter);
        _vygarHadFirstTurn.Remove(fighter);
        _vygarDamagedSinceLastTurn.Remove(fighter);
        _trooperDamageStacks.Remove(fighter);
        _lastStandUsed.Remove(fighter);
        _huronCritStacks.Remove(fighter);

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

            // 2x3 box: 2 wide (dx -1..0), 3 tall (dy -1..1), centered on Avarice's position.
            // Skip tiles that fall off the board edge (e.g. Avarice dying near a corner).
            var origin = fighter.GridPosition;
            for (int dx = -1; dx <= 0; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var pos = origin + new Vector2Int(dx, dy);
                if (_board != null && !_board.IsInBounds(pos)) continue;
                TileEffectManager.Instance.PlaceEffect(pos, vitalized, fighter);
            }

            BattleLogger.Log($"Martyr's Bloom: Vitalized zone placed at {origin}.", LogCategory.Passive);
        }

        // Trex: once, when he's the only surviving member of his team, gain a permanent +50%
        // damage multiplier and fill his sig charge. Re-checked against every living Trex on any
        // death, since it's the death of an ally (not necessarily Trex's own) that can trigger it.
        foreach (var trex in _fighterManager.AllFighters)
        {
            if (trex.FighterName != "Trex" || trex.IsDead || _lastStandUsed.Contains(trex)) continue;

            bool isLastStanding = true;
            foreach (var ally in _fighterManager.AllFighters)
            {
                if (ally == trex || ally.TeamId != trex.TeamId || ally.IsDead) continue;
                isLastStanding = false;
                break;
            }

            if (isLastStanding)
            {
                _lastStandUsed.Add(trex);
                trex.ApplyStatusEffect(new StatusEffect("Apex Predator", StatusEffectType.DamageMultiplier, "None", 0.5f, 999, false));
                trex.IncreaseCharge(trex.SigChargeReq);
                BattleLogger.Log($"{trex.FighterName} is the last of his team standing — gains +50% damage and full sig charge.", LogCategory.Passive);
            }
        }

        // Parasitic Birth — Skirvex: on death, explodes in a 3x3 box, dealing 25 Corrupt damage to
        // enemies within it. Instant and one-off, so this hits fighters directly rather than going
        // through a tile effect (nothing needs to linger).
        if (fighter.FighterName == "Skirvex")
        {
            var origin = fighter.GridPosition;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var pos    = origin + new Vector2Int(dx, dy);
                var tile   = _board?.GetTile(pos);
                var target = tile?.OccupyingCharacter?.GetComponent<Fighter>();
                if (target == null || target.IsDead || target.TeamId == fighter.TeamId) continue;

                int dealt = target.TakeDamage(25, "Corrupt", fighter);
                BattleLogger.Log($"Parasitic Birth: {target.FighterName} took {dealt} Corrupt damage.", LogCategory.Hit);
            }
        }

        // Judicial Resolve — Olthar: once per game, when an ally dies, reset his skill cooldowns
        // and gain Inscribed (30% Arcane res) + Tempered (30% Elemental res) for 4 turns.
        foreach (var ally in _fighterManager.AllFighters)
        {
            if (ally.FighterName != "Olthar") continue;
            if (ally == fighter || ally.TeamId != fighter.TeamId || ally.IsDead) continue;
            if (_judicialResolveUsed.Contains(ally)) continue;

            _judicialResolveUsed.Add(ally);
            ally.ResetSkillCooldowns();
            ally.ApplyStatusEffect(new StatusEffect("Inscribed", StatusEffectType.ResistanceModifier, "Arcane", 0.3f, 4, false));
            ally.ApplyStatusEffect(new StatusEffect("Tempered", StatusEffectType.ResistanceModifier, "Elemental", 0.3f, 4, false));
            BattleLogger.Log($"Judicial Resolve: {ally.FighterName}'s cooldowns reset; gained Inscribed and Tempered.", LogCategory.Passive);
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
            bonus.SourceFighterName = effect.SourceFighterName; // still credits Sanguine's charge on tick
            target.ApplyStatusEffect(bonus); // caster intentionally null to avoid infinite recursion
            BattleLogger.Log($"Hemorrhage: {effect.Name} applied a second time to {target.FighterName}.", LogCategory.Passive);
        }

        // Rifthunter's Focus — Faru: gains a stacking Focused buff (max 3, +15% damage mult each)
        // whenever he receives any buff from any source. Excludes Focused itself so re-applying it
        // here doesn't recursively re-trigger this same check. Duration is a large sentinel rather
        // than a real timer — Focused isn't meant to expire on its own, only be consumed by Sword
        // Strike (see DynamicValueResolver.Consume).
        if (target.FighterName == "Faru" && !effect.IsDebuff && effect.Name != "Focused")
        {
            var existing = target.State.StatusEffects.Find(e => e.Name == "Focused");
            int currentStacks = existing?.Stacks ?? 0;
            if (currentStacks < 3)
            {
                target.ApplyStatusEffect(
                    new StatusEffect("Focused", StatusEffectType.DamageMultiplier, "None", 0.15f, 999, false));
                BattleLogger.Log($"Rifthunter's Focus: {target.FighterName} gained a stack of Focused.", LogCategory.Passive);
            }
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

    private static bool HasDebuff(Fighter fighter)
    {
        foreach (var e in fighter.StatusEffects)
            if (e.IsDebuff) return true;
        return false;
    }
}
