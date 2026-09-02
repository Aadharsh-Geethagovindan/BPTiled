public enum AbilityShape
{
    Single,
    Line,
    Cone,
    Cross,
    Ring,
    Box
}

public enum AbilityTargetType
{
    Enemy,
    Ally,
    Tile,
    Ground,     // alias for Tile — targets empty board positions (e.g. tile effect placement)
    Self,
    AllyOrSelf, // hits allies and the caster themselves
    All         // hits any fighter on the tile regardless of team
}

public enum AbilitySlot
{
    Passive,
    Normal,
    Skill,
    Skill2,
    Sig
}

public enum AbilityEssence
{
    None,
    Arcane,
    Elemental,
    Force,
    Corrupt,
    True    // bypasses resistance
}

public enum TileEffectTrigger
{
    OnTurnEnd,       // fires when a fighter ends their turn on this tile
    OnEnter,         // fires each time a fighter steps onto this tile
    OnEnterDestroy,  // fires once on entry then the tile effect is consumed
    Persistent,      // applies a 1-turn hidden status at turn start while standing on tile
    OnEnterOrTurnEnd // fires both on entry and on ending a turn there (e.g. Scorched)
}

public enum TileEffectAffinity
{
    AllyOnly,  // only affects fighters on the same team as the source
    EnemyOnly, // only affects fighters on opposing teams
    All,       // affects all fighters
}

public enum DynamicValueType
{
    Damage,
    Healing,
    Shielding
}

public enum DynamicValueSource
{
    CasterBuffs,   // sum of stacks across all non-debuff status effects on the caster
    CasterDebuffs, // sum of stacks across all debuff status effects on the caster
    TargetBuffs,   // sum of stacks across all non-debuff status effects on the target
    TargetDebuffs, // sum of stacks across all debuff status effects on the target
    NamedStatus    // stacks of one specific named status on the caster (see StatusName)
}

// A damage/healing/shielding amount that scales at resolution time off status-effect counts,
// added on top of the effect/tile-effect's fixed base value. See DynamicValueResolver.
// [Serializable]: nested inside TileEffect for network sync (see TileEffect.cs / TileEffectManager).
[System.Serializable]
public class DynamicValue
{
    public DynamicValueType   ValueType;
    public DynamicValueSource Source;
    public string             StatusName;     // only used when Source == NamedStatus
    public float              AmountPerStack;
    public bool                IsConsumed;     // NamedStatus only: remove the named status after resolving

    // Parameterless constructor for JsonUtility deserialization during network sync.
    public DynamicValue() { }
}

// Describes a tile effect an ability places on the board when used.
public class AbilityTileEffect
{
    public string              Name;
    public int                 Duration;
    public TileEffectTrigger   Trigger;
    public TileEffectAffinity  Affinity;
    public int                 Damage;
    public int                 Healing;
    public int                 Shielding;
    public bool                DestroyOnTrigger;
    public System.Collections.Generic.List<AbilityStatusEffect> StatusEffectsToApply = new();
    public DynamicValue        DynamicValue;           // null if this tile effect's values are all fixed
    public string              ExcludedSpecies;        // e.g. "Riftbeast" — unaffected regardless of affinity
    public float                RemoveRandomBuffChance;// 0-1; on trigger, chance to strip one random buff
}

public enum InstantEffectType
{
    SigChargeFlat,       // positive = grant, negative = drain (flat amount)
    SigChargePercent,    // positive = grant %, negative = drain % (fraction of current charge)
    AddCooldown,         // positive = add turns to all skill cooldowns, negative = reduce
    ResetCooldown,       // clears all skill cooldowns regardless of magnitude
    TriggerDoTs,         // fires all active DoTs on the target immediately without advancing their duration
    StealBuffs,          // removes all buffs from the target and stashes them on the caster (see ReceiveStolenBuffs)
    ReceiveStolenBuffs,  // grants the target every buff the caster currently has stashed via StealBuffs
    RemoveRandomBuff,    // removes one randomly-chosen buff from the target (no stash — just gone)
    ExtendAllBuffs,      // adds Magnitude turns to the Duration of every currently-active buff on the target
}

// Describes a one-time effect an ability may trigger on hit, including its proc chance.
public class AbilityInstantEffect
{
    public InstantEffectType Type;
    public float             Magnitude;
    public float             ApplyChance; // 0.0–1.0
}

// Gates whether a status effect is applied at all, checked against pre-cast caster/target state
// before the applyChance roll (e.g. Faru's Sharpen Blade: only grant Evasive if he already has a
// buff). Reuses DynamicValueSource's counting semantics — see DynamicValueResolver.CountStacks.
// [Serializable]: nested inside AbilityStatusEffect for network sync.
[System.Serializable]
public class EffectCondition
{
    public DynamicValueSource Source;
    public string             StatusName; // only used when Source == NamedStatus
    public int                MinCount;   // minimum stack count required; <=0 treated as 1

    // Parameterless constructor for JsonUtility deserialization during network sync.
    public EffectCondition() { }
}

// Describes a status effect an ability may inflict on hit, including its proc chance.
// [Serializable]: nested inside TileEffect for network sync (see TileEffect.cs / TileEffectManager).
[System.Serializable]
public class AbilityStatusEffect
{
    public string           Name;
    public StatusEffectType Type;
    public string           Essence;
    public float            Magnitude;
    public int              Duration;
    public bool             IsDebuff;
    public float            ApplyChance; // 0.0–1.0
    public EffectCondition  Condition;   // null = always eligible (subject to ApplyChance only)
}

// One effect entry within an ability's "effects" JSON array — a single targeted outcome
// (damage/heal/shield/status/tile-effect against some TargetType, with its own shape/range).
// An ability with multiple entries (e.g. Vanguard Assault: damage the enemies in a cross, AND
// shield the caster) has one AbilityEffect per entry, not one flattened onto the Ability itself —
// see Ability.Effects.
public class AbilityEffect
{
    public AbilityTargetType TargetType;
    public AbilityShape      Shape;
    public int                Range;
    public int                MinRange;
    public int                ShapeSize;       // line length, cone rows, ring radius, cross arm length
    public int                ShapeWidth;      // Box only: width (perpendicular to facing direction)
    public int                ShapeHeight;     // Box only: height (along facing direction)
    public int                Damage;
    public int                Healing;
    public int                Shielding;
    public System.Collections.Generic.List<AbilityStatusEffect>  StatusEffectsToApply  = new();
    public System.Collections.Generic.List<AbilityInstantEffect> InstantEffectsToApply = new();
    public AbilityTileEffect  TileEffectToPlace; // null if this effect places no tile effect
    public DynamicValue       DynamicValue;      // null if this effect's values are all fixed

    // True only for an effect that needs its own independent target pick (e.g. Vemk Parlas's Sig
    // choosing which ally receives transferred buffs). False (default) means this effect resolves
    // against the SAME shapeTiles as the primary effect, filtered by its own TargetType — e.g.
    // Legionary's Sig hits enemies AND shields allies caught in the same line from one click, same
    // as how a Self-targeted effect already resolves against the caster with no separate click.
    public bool RequiresSecondaryTarget;

    // > 1 turns this effect's targeting into a multi-select: the player picks individual fighters
    // one at a time (each a Single-shape-style click, same range validation) instead of resolving
    // on the first valid click, until they've picked this many or confirm early with fewer (see
    // SelectionManager.SelectionState.MultiTargeting). Only meaningful when Shape == Single — an
    // anchor-derived AOE shape has nothing to "pick more of." Default 1 = today's normal behavior,
    // unchanged for every existing ability.
    public int MaxTargets = 1;
}

public class Ability
{
    public string          Name;
    public string          Description;
    public AbilitySlot     Slot;
    public AbilityEssence  Essence;
    public int             BaseCooldown;
    public int             CurrentCooldown;
    public int             BaseSigCharge;          // flat sig charge granted on use, regardless of effect values
    public int             Knockback;              // tiles displaced; positive = push away, negative = pull toward
    public bool            MovesUser;              // caster teleports to the anchor tile after ability resolves
    public bool            SwapWithTarget;         // caster and target exchange positions after ability resolves
    public int             RepositionRange;        // > 0: requires a second click to place the target within this range
    public System.Collections.Generic.List<AbilityEffect> Effects = new();

    public bool IsOnCooldown => CurrentCooldown > 0;

    public void SetCooldown()       => CurrentCooldown = BaseCooldown;
    public void TickCooldown()      => CurrentCooldown = UnityEngine.Mathf.Max(0, CurrentCooldown - 1);
    public void ReduceCooldown(int amount) => CurrentCooldown = UnityEngine.Mathf.Max(0, CurrentCooldown - amount);

    // Drives the targeting UI (range/shape preview, click-to-confirm): the first effect that
    // actually needs the player to pick a target. A Self-targeted effect (e.g. a shield the
    // caster grants themselves alongside a separate enemy-targeted attack) doesn't need player
    // input — it resolves automatically against the caster whenever the ability is used, so it's
    // skipped here in favor of whichever effect the player is actually aiming.
    public AbilityEffect PrimaryEffect =>
        Effects.Find(e => e.TargetType != AbilityTargetType.Self) ?? (Effects.Count > 0 ? Effects[0] : null);

    // The first effect after PrimaryEffect explicitly flagged RequiresSecondaryTarget — e.g. Vemk
    // Parlas's Sig: the primary effect removes an enemy's buffs, this one picks which ally receives
    // them. A non-Self second effect that ISN'T flagged (e.g. Legionary's Sig shielding allies in
    // the same line it damages enemies in) resolves against the primary's own shapeTiles instead,
    // same as it always has — RequiresSecondaryTarget is what distinguishes "needs its own click"
    // from "rides along with the first click, just filtered to a different TargetType." Deliberately
    // capped at one follow-up target (not full N-phase generality) — nothing in the roster needs
    // more than two targeting phases, and this mirrors how RepositionRange is already scoped to
    // exactly one follow-up.
    public AbilityEffect SecondaryEffect
    {
        get
        {
            int primaryIndex = Effects.IndexOf(PrimaryEffect);
            for (int i = primaryIndex + 1; i < Effects.Count; i++)
                if (Effects[i].RequiresSecondaryTarget)
                    return Effects[i];
            return null;
        }
    }
}
