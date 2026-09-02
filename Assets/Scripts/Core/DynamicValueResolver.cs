using UnityEngine;

// Interprets a DynamicValue spec into a concrete bonus amount at resolution time, reading live
// status-effect state off the caster/target. Shared by HitResolver (ability effects, e.g. Faru's
// Sword Strike/All Out) and TileEffectManager (tile effects, e.g. Bessil's DarkShadow).
public static class DynamicValueResolver
{
    // Pure read — does not consume/remove anything. Safe to call before a hit is confirmed.
    public static int ComputeBonus(DynamicValue spec, Fighter caster, Fighter target)
    {
        if (spec == null) return 0;
        return Mathf.RoundToInt(CountStacks(spec.Source, spec.StatusName, caster, target) * spec.AmountPerStack);
    }

    // Pure read. Returns true if there's no condition to satisfy (i.e. always eligible).
    public static bool ConditionMet(EffectCondition condition, Fighter caster, Fighter target)
    {
        if (condition == null) return true;
        int required = Mathf.Max(1, condition.MinCount);
        return CountStacks(condition.Source, condition.StatusName, caster, target) >= required;
    }

    // Shared stack-counting logic behind both ComputeBonus and ConditionMet.
    public static int CountStacks(DynamicValueSource source, string statusName, Fighter caster, Fighter target)
    {
        return source switch
        {
            DynamicValueSource.NamedStatus    => GetNamedStacks(caster, statusName),
            DynamicValueSource.CasterBuffs    => SumStacks(caster, isDebuff: false),
            DynamicValueSource.CasterDebuffs  => SumStacks(caster, isDebuff: true),
            DynamicValueSource.TargetBuffs    => SumStacks(target, isDebuff: false),
            DynamicValueSource.TargetDebuffs  => SumStacks(target, isDebuff: true),
            _                                 => 0
        };
    }

    // Removes the named status this spec consumed. No-op for non-NamedStatus sources — "consume
    // all buffs/debuffs" isn't a case any current fighter needs, so it's left unimplemented rather
    // than guessed at.
    public static void Consume(DynamicValue spec, Fighter caster)
    {
        if (spec == null || !spec.IsConsumed || spec.Source != DynamicValueSource.NamedStatus) return;
        if (string.IsNullOrEmpty(spec.StatusName)) return;
        caster.RemoveStatusEffect(spec.StatusName);
    }

    private static int GetNamedStacks(Fighter fighter, string statusName)
    {
        if (fighter == null || string.IsNullOrEmpty(statusName)) return 0;
        var effect = fighter.State.StatusEffects.Find(e => e.Name == statusName);
        return effect?.Stacks ?? 0;
    }

    private static int SumStacks(Fighter fighter, bool isDebuff)
    {
        if (fighter == null) return 0;
        int total = 0;
        foreach (var e in fighter.State.StatusEffects)
            if (e.IsDebuff == isDebuff)
                total += e.Stacks;
        return total;
    }
}
