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
    // Sig charge weights — tune these to rebalance charge gain per action type.
    private const float DamageChargeWeight  = 1f;
    private const float HealingChargeWeight = 1f;
    private const float ShieldChargeWeight  = 1f;

    // Convenience: calculate + apply in one call (used until animation system exists).
    public static void Execute(Fighter caster, Ability ability, List<Vector2Int> shapeTiles, Board board)
    {
        var results = Calculate(caster, ability, shapeTiles, board);
        Apply(caster, ability, results, board);
        PlaceTileEffects(caster, ability, shapeTiles);
    }

    private static void PlaceTileEffects(Fighter caster, Ability ability, List<Vector2Int> shapeTiles)
    {
        if (ability.TileEffectToPlace == null || TileEffectManager.Instance == null) return;
        foreach (var pos in shapeTiles)
            TileEffectManager.Instance.PlaceEffect(pos, ability.TileEffectToPlace, caster.TeamId);
    }

    // ── Step 1: Calculate ──────────────────────────────────────────────────
    // Pure — no state changes. Safe to call before animations play.

    public static List<HitResult> Calculate(Fighter caster, Ability ability,
                                            List<Vector2Int> shapeTiles, Board board)
    {
        var results = new List<HitResult>();

        foreach (var pos in shapeTiles)
        {
            var tile = board.GetTile(pos);
            if (tile?.OccupyingCharacter == null) continue;

            var target = tile.OccupyingCharacter.GetComponent<Fighter>();
            if (target == null || target.IsDead) continue;
            if (!IsValidTarget(caster, target, ability.TargetType)) continue;

            results.Add(HitResolver.Calculate(caster, target, ability));
        }

        return results;
    }

    // ── Step 2: Apply ──────────────────────────────────────────────────────
    // Commits all results — mutates fighter state and fires events.

    public static void Apply(Fighter caster, Ability ability, List<HitResult> results, Board board)
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

            string critTag = result.IsCrit ? " (CRIT)" : "";

            if (result.FinalDamage > 0)
            {
                int dealt = result.Target.TakeDamage(result.FinalDamage);
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
                                BattleLogger.Log($"{result.Target.FighterName}'s DoTs triggered for {totalDoTDmg} bonus damage.", LogCategory.Effect);
                            break;
                    }
                }
            }

            if (ability.Knockback != 0)
                DisplacementResolver.Resolve(caster, result.Target, ability.Knockback, board);
        }

        int chargeToGrant = ability.BaseSigCharge > 0 ? ability.BaseSigCharge : totalCharge;
        caster.IncreaseCharge(chargeToGrant);

        if (ability.SwapWithTarget && results.Count > 0)
        {
            var hit = results[0];
            if (hit.IsHit && !hit.Target.IsDead)
                SwapPositions(caster, hit.Target, board);
        }
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

    private static bool IsValidTarget(Fighter caster, Fighter target, AbilityTargetType targetType)
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
