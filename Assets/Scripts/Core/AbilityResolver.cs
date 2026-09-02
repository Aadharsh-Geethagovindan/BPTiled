using System;
using System.Collections.Generic;
using UnityEngine;

// [SERVER] Resolves ability effects against targets on the board.
// Called only from BattleController.RequestUseAbility.
//
// Flow:
//   1. Calculate(...)  — produces HitResult list, no state mutation (safe for animation pre-roll)
//   2. Apply(...)      — commits results, mutates fighter state, fires events
public static class AbilityResolver
{
    // Sig charge weights — tune these to rebalance charge gain per action type. Public because
    // TileEffectManager grants charge to a tile effect's original placer using these same
    // weights, so a fighter earns charge consistently whether damage/healing/shielding came from
    // a direct hit or from a zone they placed going off later.
    public const float DamageChargeWeight  = 1f;
    public const float HealingChargeWeight = 1f;
    public const float ShieldChargeWeight  = 1f;

    // Fires once per ability use, after Apply() has fully committed it — regardless of hit/miss
    // (using the ability is the trigger, e.g. K.A.S.'s Overdrive Matrix cares about casting the
    // Sig, not landing it). [SERVER]-only, same as everything else in this file.
    public static event Action<Fighter, Ability> OnAbilityUsed;

    // Fires once per confirmed hit that lands on a fighter from a different team. Unlike
    // Fighter.OnFighterDamaged (which only exposes the target, not who dealt it), this exposes the
    // caster — needed for passives that react to landing hits rather than taking them (e.g. Breach
    // Specialist's "every 5 hits on enemy targets").
    public static event Action<Fighter, Fighter> OnEnemyHit;

    // Fires once per hit that lands as a crit (e.g. Mizca's Rage stacks).
    public static event Action<Fighter> OnCrit;

    // Fires once per action that strips one or more buffs off a target (StealBuffs or
    // RemoveRandomBuff), passing how many were actually removed. E.g. Vemk Parlas's Sabotaged
    // Advantage rolls a per-buff chance off this.
    public static event Action<Fighter, int> OnBuffRemoved;

    // Convenience: calculate + apply in one call (used until animation system exists).
    //
    // onlyEffect / grantChargeAndFireEvent exist for abilities with a SecondaryEffect (e.g. Vemk
    // Parlas's Sig): the primary pass resolves everything except the deferred secondary effect and
    // (as normal) grants charge / fires OnAbilityUsed once; the later secondary-effect pass — once
    // its own target is picked — resolves only that one effect and skips those "once per ability
    // use" side effects, since they already happened on the primary pass. For every ability without
    // a SecondaryEffect this is a complete no-op — SecondaryEffect is null, so nothing is skipped.
    public static void Execute(Fighter caster, Ability ability, List<Vector2Int> shapeTiles, Board board,
                                AbilityEffect onlyEffect = null, bool grantChargeAndFireEvent = true)
    {
        var results = Calculate(caster, ability, shapeTiles, board, onlyEffect);
        Apply(caster, ability, results, board, grantChargeAndFireEvent);
        PlaceTileEffects(caster, ability, shapeTiles, onlyEffect);
    }

    private static void PlaceTileEffects(Fighter caster, Ability ability, List<Vector2Int> shapeTiles, AbilityEffect onlyEffect)
    {
        if (TileEffectManager.Instance == null) return;

        foreach (var effect in ability.Effects)
        {
            if (!ShouldResolve(effect, ability, onlyEffect)) continue;
            if (effect.TileEffectToPlace == null) continue;
            foreach (var pos in shapeTiles)
                TileEffectManager.Instance.PlaceEffect(pos, effect.TileEffectToPlace, caster);
        }
    }

    // Filters which of an ability's effects a given resolution pass should process: onlyEffect
    // set → just that one (the secondary-effect pass); onlyEffect null → everything except the
    // ability's own SecondaryEffect, if it has one (the primary pass, deferring the follow-up).
    private static bool ShouldResolve(AbilityEffect effect, Ability ability, AbilityEffect onlyEffect)
    {
        if (onlyEffect != null) return effect == onlyEffect;
        return effect != ability.SecondaryEffect;
    }

    // ── Step 1: Calculate ──────────────────────────────────────────────────
    // Pure — no state changes. Safe to call before animations play.

    public static List<HitResult> Calculate(Fighter caster, Ability ability,
                                            List<Vector2Int> shapeTiles, Board board, AbilityEffect onlyEffect = null)
    {
        var results = new List<HitResult>();

        foreach (var effect in ability.Effects)
        {
            if (!ShouldResolve(effect, ability, onlyEffect)) continue;

            // Self-targeted effects (e.g. a shield the caster grants themselves alongside a
            // separate enemy-targeted attack) don't go through the player-picked shapeTiles at
            // all — they always resolve directly against the caster.
            if (effect.TargetType == AbilityTargetType.Self)
            {
                results.Add(HitResolver.Calculate(caster, caster, ability, effect));
                continue;
            }

            foreach (var pos in shapeTiles)
            {
                var tile = board.GetTile(pos);
                if (tile?.OccupyingCharacter == null) continue;

                var target = tile.OccupyingCharacter.GetComponent<Fighter>();
                if (target == null || target.IsDead) continue;
                if (!IsValidTarget(caster, target, effect.TargetType)) continue;

                results.Add(HitResolver.Calculate(caster, target, ability, effect));
            }
        }

        return results;
    }

    // ── Step 2: Apply ──────────────────────────────────────────────────────
    // Commits all results — mutates fighter state and fires events.

    public static void Apply(Fighter caster, Ability ability, List<HitResult> results, Board board, bool grantChargeAndFireEvent = true)
    {
        int totalCharge = 0;

        foreach (var result in results)
        {
            if (!result.IsHit)
            {
                BattleLogger.Log($"{caster.FighterName} missed {result.Target.FighterName} with {ability.Name}.", LogCategory.Miss);
                continue;
            }

            BattleLogger.Log($"{caster.FighterName} used {ability.Name} on {result.Target.FighterName}.", LogCategory.Ability);

            if (result.Target.TeamId != caster.TeamId)
                OnEnemyHit?.Invoke(caster, result.Target);

            if (result.IsCrit)
                OnCrit?.Invoke(caster);

            string critTag = result.IsCrit ? " (CRIT)" : "";

            if (result.FinalDamage > 0)
            {
                int dealt = result.Target.TakeDamage(result.FinalDamage, ability.Essence.ToString(), caster);
                caster.AddDamageDealt(dealt);
                totalCharge += Mathf.RoundToInt(dealt * DamageChargeWeight);
                BattleLogger.Log($"{result.Target.FighterName} took {dealt}{critTag} {ability.Essence} damage. " +
                                 $"({result.Target.CurrentHP}/{result.Target.MaxHP} HP)", LogCategory.Hit);
            }

            if (result.FinalHealing > 0)
            {
                int healed = result.Target.Heal(result.FinalHealing);
                totalCharge += Mathf.RoundToInt(healed * HealingChargeWeight);
                BattleLogger.Log($"{result.Target.FighterName} was healed for {healed} HP. " +
                                 $"({result.Target.CurrentHP}/{result.Target.MaxHP} HP)", LogCategory.Hit);
            }

            if (result.FinalShielding > 0)
            {
                int shielded = result.Target.AddShield(result.FinalShielding);
                totalCharge += Mathf.RoundToInt(shielded * ShieldChargeWeight);
                BattleLogger.Log($"{result.Target.FighterName} gained {shielded} shield. (Total: {result.Target.Shield})", LogCategory.Hit);
            }

            if (result.StatusEffectsToApply != null)
            {
                foreach (var effect in result.StatusEffectsToApply)
                {
                    result.Target.ApplyStatusEffect(effect, caster);
                    BattleLogger.Log($"{effect.Name} applied to {result.Target.FighterName} ({effect.Duration} turns).", LogCategory.Effect);
                }
            }

            // Dynamic value consumption (e.g. Faru's Sword Strike spending his Focused stacks) —
            // deferred here rather than done in HitResolver.Calculate, which is pure/no-mutation.
            DynamicValueResolver.Consume(result.Effect.DynamicValue, caster);

            if (result.InstantEffectsToApply != null)
            {
                foreach (var ie in result.InstantEffectsToApply)
                {
                    switch (ie.Type)
                    {
                        case InstantEffectType.SigChargeFlat:
                            result.Target.ModifyChargeFlat(Mathf.RoundToInt(ie.Magnitude));
                            BattleLogger.Log($"{result.Target.FighterName}'s charge modified by {ie.Magnitude} (flat).", LogCategory.Effect);
                            break;
                        case InstantEffectType.SigChargePercent:
                            result.Target.ModifyChargePercent(ie.Magnitude);
                            BattleLogger.Log($"{result.Target.FighterName}'s charge modified by {ie.Magnitude * 100f:0}%.", LogCategory.Effect);
                            break;
                        case InstantEffectType.AddCooldown:
                            result.Target.AddCooldownToSkills(Mathf.RoundToInt(ie.Magnitude));
                            BattleLogger.Log($"{result.Target.FighterName}'s skill cooldowns modified by {ie.Magnitude} turns.", LogCategory.Effect);
                            break;
                        case InstantEffectType.ResetCooldown:
                            result.Target.ResetSkillCooldowns();
                            BattleLogger.Log($"{result.Target.FighterName}'s skill cooldowns were reset.", LogCategory.Effect);
                            break;
                        case InstantEffectType.TriggerDoTs:
                            int totalDoTDmg = 0;
                            foreach (var se in result.Target.StatusEffects)
                                totalDoTDmg += se.TriggerDamageOnly(result.Target);
                            if (totalDoTDmg > 0)
                            {
                                // This damage is a direct result of the caster's move (detonating
                                // the DoTs), so it counts toward their charge same as a direct hit.
                                totalCharge += Mathf.RoundToInt(totalDoTDmg * DamageChargeWeight);
                                BattleLogger.Log($"{result.Target.FighterName}'s DoTs triggered for {totalDoTDmg} bonus damage.", LogCategory.Effect);
                            }
                            break;
                        case InstantEffectType.StealBuffs:
                        {
                            var stolen = result.Target.RemoveAllBuffs();
                            caster.StashTransferredBuffs(stolen);
                            if (stolen.Count > 0)
                            {
                                BattleLogger.Log($"{caster.FighterName} stole {stolen.Count} buff(s) from {result.Target.FighterName}.", LogCategory.Effect);
                                OnBuffRemoved?.Invoke(caster, stolen.Count);
                            }
                            break;
                        }
                        case InstantEffectType.ReceiveStolenBuffs:
                        {
                            var buffs = caster.TakeTransferredBuffs();
                            foreach (var buff in buffs)
                                result.Target.ApplyStatusEffect(buff, caster);
                            if (buffs.Count > 0)
                                BattleLogger.Log($"{result.Target.FighterName} received {buffs.Count} transferred buff(s).", LogCategory.Effect);
                            break;
                        }
                        case InstantEffectType.RemoveRandomBuff:
                        {
                            var removed = result.Target.RemoveRandomStatusEffect(isDebuff: false);
                            if (removed != null)
                            {
                                BattleLogger.Log($"{result.Target.FighterName} lost {removed}.", LogCategory.Effect);
                                OnBuffRemoved?.Invoke(caster, 1);
                            }
                            break;
                        }
                        case InstantEffectType.ExtendAllBuffs:
                        {
                            int extended = 0;
                            foreach (var se in result.Target.StatusEffects)
                            {
                                if (se.IsDebuff) continue;
                                se.Duration += Mathf.RoundToInt(ie.Magnitude);
                                extended++;
                            }
                            if (extended > 0)
                                BattleLogger.Log($"{result.Target.FighterName}'s buffs extended by {ie.Magnitude} turn(s).", LogCategory.Effect);
                            break;
                        }
                    }
                }
            }

            // Knockback only makes sense for effects that hit someone other than the caster —
            // skip it for Self-targeted effects (e.g. Vanguard Assault's self-shield shouldn't
            // try to displace the caster relative to themselves).
            if (ability.Knockback != 0 && result.Effect.TargetType != AbilityTargetType.Self)
                DisplacementResolver.Resolve(caster, result.Target, ability.Knockback, board);
        }

        if (grantChargeAndFireEvent)
        {
            int chargeToGrant = ability.BaseSigCharge > 0 ? ability.BaseSigCharge : totalCharge;
            caster.IncreaseCharge(chargeToGrant);
        }

        if (ability.SwapWithTarget && results.Count > 0)
        {
            var hit = results[0];
            if (hit.IsHit && !hit.Target.IsDead)
                SwapPositions(caster, hit.Target, board);
        }

        if (grantChargeAndFireEvent)
            OnAbilityUsed?.Invoke(caster, ability);
    }

    private static void SwapPositions(Fighter a, Fighter b, Board board)
    {
        var posA  = a.GridPosition;
        var posB  = b.GridPosition;
        var tileA = board.GetTile(posA);
        var tileB = board.GetTile(posB);

        if (tileA != null) tileA.OccupyingCharacter = b.gameObject;
        if (tileB != null) tileB.OccupyingCharacter = a.gameObject;

        a.SetGridPosition(posB);
        b.SetGridPosition(posA);

        BattleLogger.Log($"{a.FighterName} and {b.FighterName} swapped positions.", LogCategory.Movement);
    }

    // ── Target validation ──────────────────────────────────────────────────

    // Internal-visibility (not private) so SelectionManager can reuse the exact same rule when
    // validating a multi-select pick client-side, instead of duplicating this switch.
    public static bool IsValidTarget(Fighter caster, Fighter target, AbilityTargetType targetType)
    {
        return targetType switch
        {
            AbilityTargetType.Enemy      => target.TeamId != caster.TeamId,
            AbilityTargetType.Ally       => target.TeamId == caster.TeamId && target != caster,
            AbilityTargetType.Self       => target == caster,
            AbilityTargetType.AllyOrSelf => target.TeamId == caster.TeamId,
            AbilityTargetType.All        => true,
            AbilityTargetType.Tile       => true,
            AbilityTargetType.Ground     => true,
            _                            => false
        };
    }
}
