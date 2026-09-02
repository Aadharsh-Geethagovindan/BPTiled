using System.Collections.Generic;
using UnityEngine;

// Calculates the outcome of one ability effect against a single target.
// Pure calculation — no state mutation. Safe to call before animations play.
public static class HitResolver
{
    // Minimum hit chance regardless of accuracy/dodge values.
    // Prevents abilities from becoming literally impossible to land.
    private const float MinHitChance = 0.05f;

    public static HitResult Calculate(Fighter caster, Fighter target, Ability ability, AbilityEffect effect)
    {
        // Effects targeting allies/self always land — only enemy-targeted effects roll hit
        bool alwaysHits = effect.TargetType == AbilityTargetType.Ally
                       || effect.TargetType == AbilityTargetType.Self
                       || effect.TargetType == AbilityTargetType.AllyOrSelf
                       || effect.TargetType == AbilityTargetType.Tile;

        bool isHit = alwaysHits || RollHit(caster, target);

        // A dynamic value can make an effect deal damage even when its fixed base is 0 (not used
        // by any current fighter, but keeps crit-eligibility correct for future ones).
        bool dealsDamage = effect.Damage > 0
                        || (effect.DynamicValue != null && effect.DynamicValue.ValueType == DynamicValueType.Damage);
        bool isCrit = isHit && dealsDamage && RollCrit(caster);

        int finalDamage    = 0;
        int finalHealing   = 0;
        int finalShielding = 0;
        List<StatusEffect>        procdStatusEffects  = null;
        List<AbilityInstantEffect> procdInstantEffects = null;

        if (isHit)
        {
            // Dynamic bonus is folded into the base value before the damage pipeline runs, so it
            // scales with damage multiplier/essence bonus/crit/resistance exactly like a bigger
            // fixed Damage/Healing/Shielding value would — not a flat post-mitigation add-on.
            int dynamicDamage = 0, dynamicHealing = 0, dynamicShielding = 0;
            if (effect.DynamicValue != null)
            {
                int bonus = DynamicValueResolver.ComputeBonus(effect.DynamicValue, caster, target);
                switch (effect.DynamicValue.ValueType)
                {
                    case DynamicValueType.Damage:    dynamicDamage    = bonus; break;
                    case DynamicValueType.Healing:   dynamicHealing   = bonus; break;
                    case DynamicValueType.Shielding: dynamicShielding = bonus; break;
                }
            }

            if (dealsDamage)
                finalDamage    = CalculateDamage(caster, target, ability, effect, isCrit, effect.Damage + dynamicDamage);

            if (effect.Healing > 0 || dynamicHealing > 0)
                finalHealing   = effect.Healing + dynamicHealing;

            if (effect.Shielding > 0 || dynamicShielding > 0)
                finalShielding = effect.Shielding + dynamicShielding;

            procdStatusEffects  = RollStatusEffects(caster, target, effect);
            procdInstantEffects = RollInstantEffects(effect);
        }

        return new HitResult(caster, target, effect, isHit, isCrit,
                             finalDamage, finalHealing, finalShielding,
                             procdStatusEffects, procdInstantEffects);
    }

    // ── Rolls ──────────────────────────────────────────────────────────────

    private static bool RollHit(Fighter caster, Fighter target)
    {
        float hitChance = Mathf.Max(MinHitChance,
                              caster.GetModifiedAccuracy() - target.GetModifiedDodge());
        return Random.value < hitChance;
    }

    private static bool RollCrit(Fighter caster)
    {
        return Random.value < caster.GetModifiedCritRate();
    }

    // Returns only the status effects that passed their condition (if any) and applyChance roll.
    // Condition is checked against pre-cast state — caster/target haven't been mutated by this
    // ability yet at this point, so e.g. Faru's Sharpen Blade correctly reads his buffs from
    // before Sharpened itself would be granted, not after.
    private static List<StatusEffect> RollStatusEffects(Fighter caster, Fighter target, AbilityEffect effect)
    {
        if (effect.StatusEffectsToApply == null || effect.StatusEffectsToApply.Count == 0)
            return null;

        var result = new List<StatusEffect>();
        foreach (var se in effect.StatusEffectsToApply)
        {
            if (!DynamicValueResolver.ConditionMet(se.Condition, caster, target)) continue;
            if (Random.value <= se.ApplyChance)
                result.Add(new StatusEffect(se.Name, se.Type, se.Essence, se.Magnitude, se.Duration, se.IsDebuff));
        }
        return result;
    }

    // Returns only the instant effects whose applyChance roll succeeded.
    private static List<AbilityInstantEffect> RollInstantEffects(AbilityEffect effect)
    {
        if (effect.InstantEffectsToApply == null || effect.InstantEffectsToApply.Count == 0)
            return null;

        var result = new List<AbilityInstantEffect>();
        foreach (var ie in effect.InstantEffectsToApply)
        {
            if (Random.value <= ie.ApplyChance)
                result.Add(ie);
        }
        return result;
    }

    // ── Damage formula ─────────────────────────────────────────────────────

    private static int CalculateDamage(Fighter caster, Fighter target, Ability ability, AbilityEffect effect, bool isCrit, int baseDamage)
    {
        if (ability.Essence == AbilityEssence.True)
        {
            // True damage: no multipliers, no resistance, crit still applies
            float trueDmg = baseDamage * (isCrit ? caster.GetModifiedCritDmg() : 1f);
            return Mathf.RoundToInt(trueDmg);
        }

        float dmgMult      = caster.GetModifiedDamageMultiplier();
        float essenceBonus = caster.GetEssenceDmgBonus(ability.Essence);
        float resistance   = target.GetModifiedResistance(ability.Essence);
        float critMult     = isCrit ? caster.GetModifiedCritDmg() : 1f;

        float damage = baseDamage * dmgMult * (1f + essenceBonus) * critMult * (1f - resistance);
        return Mathf.Max(0, Mathf.RoundToInt(damage));
    }
}
