using System.Collections.Generic;
using UnityEngine;

// Calculates the outcome of an ability against a single target.
// Pure calculation — no state mutation. Safe to call before animations play.
public static class HitResolver
{
    // Minimum hit chance regardless of accuracy/dodge values.
    // Prevents abilities from becoming literally impossible to land.
    private const float MinHitChance = 0.05f;

    public static HitResult Calculate(Fighter caster, Fighter target, Ability ability)
    {
        // Abilities targeting allies/self always land — only enemy-targeted abilities roll hit
        bool alwaysHits = ability.TargetType == AbilityTargetType.Ally
                       || ability.TargetType == AbilityTargetType.Self
                       || ability.TargetType == AbilityTargetType.AllyOrSelf
                       || ability.TargetType == AbilityTargetType.Tile;

        bool isHit  = alwaysHits || RollHit(caster, target);
        bool isCrit = isHit && ability.Damage > 0 && RollCrit(caster);

        int finalDamage    = 0;
        int finalHealing   = 0;
        int finalShielding = 0;
        List<StatusEffect>        procdStatusEffects  = null;
        List<AbilityInstantEffect> procdInstantEffects = null;

        if (isHit)
        {
            if (ability.Damage > 0)
                finalDamage    = CalculateDamage(caster, target, ability, isCrit);

            if (ability.Healing > 0)
                finalHealing   = ability.Healing;

            if (ability.Shielding > 0)
                finalShielding = ability.Shielding;

            procdStatusEffects  = RollStatusEffects(ability);
            procdInstantEffects = RollInstantEffects(ability);
        }

        return new HitResult(caster, target, ability, isHit, isCrit,
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

    // Returns only the status effects whose applyChance roll succeeded.
    private static List<StatusEffect> RollStatusEffects(Ability ability)
    {
        if (ability.StatusEffectsToApply == null || ability.StatusEffectsToApply.Count == 0)
            return null;

        var result = new List<StatusEffect>();
        foreach (var se in ability.StatusEffectsToApply)
        {
            if (Random.value <= se.ApplyChance)
                result.Add(new StatusEffect(se.Name, se.Type, se.Essence, se.Magnitude, se.Duration, se.IsDebuff));
        }
        return result;
    }

    // Returns only the instant effects whose applyChance roll succeeded.
    private static List<AbilityInstantEffect> RollInstantEffects(Ability ability)
    {
        if (ability.InstantEffectsToApply == null || ability.InstantEffectsToApply.Count == 0)
            return null;

        var result = new List<AbilityInstantEffect>();
        foreach (var ie in ability.InstantEffectsToApply)
        {
            if (Random.value <= ie.ApplyChance)
                result.Add(ie);
        }
        return result;
    }

    // ── Damage formula ─────────────────────────────────────────────────────

    private static int CalculateDamage(Fighter caster, Fighter target, Ability ability, bool isCrit)
    {
        if (ability.Essence == AbilityEssence.True)
        {
            // True damage: no multipliers, no resistance, crit still applies
            float trueDmg = ability.Damage * (isCrit ? caster.GetModifiedCritDmg() : 1f);
            return Mathf.RoundToInt(trueDmg);
        }

        float dmgMult      = caster.GetModifiedDamageMultiplier();
        float essenceBonus = caster.GetEssenceDmgBonus(ability.Essence);
        float resistance   = target.GetModifiedResistance(ability.Essence);
        float critMult     = isCrit ? caster.GetModifiedCritDmg() : 1f;

        float damage = ability.Damage * dmgMult * (1f + essenceBonus) * critMult * (1f - resistance);
        return Mathf.Max(0, Mathf.RoundToInt(damage));
    }
}
