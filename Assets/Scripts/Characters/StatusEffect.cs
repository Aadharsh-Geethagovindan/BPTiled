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
    Root,
    DamageRedirect, // Magnitude = fraction of incoming damage rerouted to SourceFighterName (e.g. JudgeWard)
    Stun // marker only for now — nothing applies it or gates a stunned fighter's turn yet; exists so
         // immunity checks (e.g. Bessil's Nightmare's Grasp) have a real type to reference
}

// Runtime representation of an active status effect on a fighter.
// Created when an effect is applied; tracked until Duration reaches 0.
// [Serializable] is required for JsonUtility to include this correctly when it's nested inside
// FighterState.StatusEffects during network sync (see FighterState.cs / BattleNetworkBridge).
[System.Serializable]
public class StatusEffect
{
    public string           Name;
    public StatusEffectType Type;
    public string           Essence;   // "Arcane" | "Elemental" | "Force" | "Corrupt" | "True" | "None"
    public float            Magnitude;
    public int              Duration;  // remaining turns
    public bool             IsDebuff;
    public int              Stacks;    // 1 by default; increments when the same effect is re-applied
    public string           SourceFighterName; // who applied it — grants them charge on each DoT/HoT tick

    public bool IsPeriodic => Type == StatusEffectType.DamageOverTime
                           || Type == StatusEffectType.HealOverTime;

    // False for internally-applied tile-based stat mods (Stormy, Uprooted, etc.)
    // that are already visible on the tile info panel — prevents double-counting in fighter chips.
    public bool ToDisplay = true;

    // Parameterless constructor for JsonUtility deserialization during network sync.
    public StatusEffect() { }

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
            {
                var dotSource = !string.IsNullOrEmpty(SourceFighterName) ? FighterManager.Instance?.GetFighterByName(SourceFighterName) : null;
                int dealt = target.TakeDamage(UnityEngine.Mathf.RoundToInt(Magnitude * Stacks), Essence, dotSource);
                BattleLogger.Log($"{target.FighterName} took {dealt} {Name} damage. ({target.CurrentHP}/{target.MaxHP} HP)", LogCategory.Hit);
                GrantChargeToSource(UnityEngine.Mathf.RoundToInt(dealt * AbilityResolver.DamageChargeWeight));
                break;
            }
            case StatusEffectType.HealOverTime:
            {
                int healed = target.Heal(UnityEngine.Mathf.RoundToInt(Magnitude * Stacks));
                BattleLogger.Log($"{target.FighterName} healed {healed} HP from {Name}.", LogCategory.Hit);
                GrantChargeToSource(UnityEngine.Mathf.RoundToInt(healed * AbilityResolver.HealingChargeWeight));
                break;
            }
        }
        Duration--;
        return Duration <= 0;
    }

    // Grants charge to whoever applied this effect — same weights as a direct hit, so a fighter
    // earns charge consistently whether the damage/heal came from their own attack or from a DoT/HoT
    // they applied ticking on a later turn. No-op if there's no recorded source or it has since died.
    private void GrantChargeToSource(int amount)
    {
        if (amount <= 0 || string.IsNullOrEmpty(SourceFighterName)) return;
        var source = FighterManager.Instance?.GetFighterByName(SourceFighterName);
        if (source != null && !source.IsDead)
            source.IncreaseCharge(amount);
    }

    // Fires the DoT damage without consuming a duration tick.
    // Used by abilities that detonate all DoTs on hit (e.g. Exsanguinate).
    public int TriggerDamageOnly(Fighter target)
    {
        if (Type != StatusEffectType.DamageOverTime) return 0;
        int dmg = UnityEngine.Mathf.RoundToInt(Magnitude * Stacks);
        var dotSource = !string.IsNullOrEmpty(SourceFighterName) ? FighterManager.Instance?.GetFighterByName(SourceFighterName) : null;
        target.TakeDamage(dmg, Essence, dotSource);
        return dmg;
    }
}
