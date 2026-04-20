using System.Collections.Generic;

// Immutable result of a single hit calculation.
// Produced by HitResolver.Calculate — contains the full outcome before any state mutation.
// Pass to AbilityResolver.Apply to commit the changes.
public class HitResult
{
    public readonly Fighter Caster;
    public readonly Fighter Target;
    public readonly Ability Ability;

    public readonly bool IsHit;
    public readonly bool IsCrit;

    public readonly int FinalDamage;    // 0 if miss or ability has no damage
    public readonly int FinalHealing;   // heals always land
    public readonly int FinalShielding; // shields always land

    // Status effects that passed their applyChance roll — ready to commit in Apply().
    public readonly IReadOnlyList<StatusEffect> StatusEffectsToApply;

    // Instant effects that passed their applyChance roll — ready to commit in Apply().
    public readonly IReadOnlyList<AbilityInstantEffect> InstantEffectsToApply;

    public HitResult(Fighter caster, Fighter target, Ability ability,
                     bool isHit, bool isCrit,
                     int finalDamage, int finalHealing, int finalShielding,
                     List<StatusEffect> statusEffectsToApply,
                     List<AbilityInstantEffect> instantEffectsToApply)
    {
        Caster                = caster;
        Target                = target;
        Ability               = ability;
        IsHit                 = isHit;
        IsCrit                = isCrit;
        FinalDamage           = finalDamage;
        FinalHealing          = finalHealing;
        FinalShielding        = finalShielding;
        StatusEffectsToApply  = statusEffectsToApply  ?? new List<StatusEffect>();
        InstantEffectsToApply = instantEffectsToApply ?? new List<AbilityInstantEffect>();
    }
}
