public enum StatusEffectType
{
    DamageOverTime,
    HealOverTime,
    AccuracyModifier,
    DodgeModifier,
    CritRateModifier,
    CritDamageModifier,
    DamageMultiplier,
    ResistanceModifier,
    SpeedModifier,
    Root
}

// Runtime representation of an active status effect on a fighter.
// Created when an effect is applied; tracked until Duration reaches 0.
public class StatusEffect
{
    public string           Name;
    public StatusEffectType Type;
    public string           Essence;   // "Arcane" | "Elemental" | "Force" | "Corrupt" | "True" | "None"
    public float            Magnitude;
    public int              Duration;  // remaining turns
    public bool             IsDebuff;
    public int              Stacks;    // 1 by default; increments when the same effect is re-applied

    public bool IsPeriodic => Type == StatusEffectType.DamageOverTime
                           || Type == StatusEffectType.HealOverTime;

    // False for internally-applied tile-based stat mods (Stormy, Uprooted, etc.)
    // that are already visible on the tile info panel — prevents double-counting in fighter chips.
    public bool ToDisplay = true;

    public StatusEffect(string name, StatusEffectType type, string essence, float magnitude, int duration, bool isDebuff, bool toDisplay = true)
    {
        Name      = name;
        Type      = type;
        Essence   = essence ?? "None";
        Magnitude = magnitude;
        Duration  = duration;
        IsDebuff  = isDebuff;
        Stacks    = 1;
        ToDisplay = toDisplay;
    }

    // Applies the periodic tick to the target. Only meaningful for DoT/HoT.
    // Returns true if this effect has expired and should be removed.
    public bool Apply(Fighter target)
    {
        switch (Type)
        {
            case StatusEffectType.DamageOverTime:
                target.TakeDamage(UnityEngine.Mathf.RoundToInt(Magnitude * Stacks));
                break;
            case StatusEffectType.HealOverTime:
                target.Heal(UnityEngine.Mathf.RoundToInt(Magnitude * Stacks));
                break;
        }
        Duration--;
        return Duration <= 0;
    }

    // Fires the DoT damage without consuming a duration tick.
    // Used by abilities that detonate all DoTs on hit (e.g. Exsanguinate).
    public int TriggerDamageOnly(Fighter target)
    {
        if (Type != StatusEffectType.DamageOverTime) return 0;
        int dmg = UnityEngine.Mathf.RoundToInt(Magnitude * Stacks);
        target.TakeDamage(dmg);
        return dmg;
    }
}
