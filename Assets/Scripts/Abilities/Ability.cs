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
}

public enum TileEffectAffinity
{
    AllyOnly,  // only affects fighters on the same team as the source
    EnemyOnly, // only affects fighters on opposing teams
    All,       // affects all fighters
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
}

public enum InstantEffectType
{
    SigChargeFlat,    // positive = grant, negative = drain (flat amount)
    SigChargePercent, // positive = grant %, negative = drain % (fraction of current charge)
    AddCooldown,      // positive = add turns to all skill cooldowns, negative = reduce
    ResetCooldown,    // clears all skill cooldowns regardless of magnitude
    TriggerDoTs,      // fires all active DoTs on the target immediately without advancing their duration
}

// Describes a one-time effect an ability may trigger on hit, including its proc chance.
public class AbilityInstantEffect
{
    public InstantEffectType Type;
    public float             Magnitude;
    public float             ApplyChance; // 0.0–1.0
}

// Describes a status effect an ability may inflict on hit, including its proc chance.
public class AbilityStatusEffect
{
    public string           Name;
    public StatusEffectType Type;
    public string           Essence;
    public float            Magnitude;
    public int              Duration;
    public bool             IsDebuff;
    public float            ApplyChance; // 0.0–1.0
}

public class Ability
{
    public string          Name;
    public string          Description;
    public AbilitySlot     Slot;
    public AbilityEssence  Essence;
    public AbilityShape    Shape;
    public int             Range;
    public int             MinRange;
    public int             ShapeSize;       // line length, cone rows, ring radius, cross arm length
    public int             ShapeWidth;      // Box only: width (perpendicular to facing direction)
    public int             ShapeHeight;     // Box only: height (along facing direction)
    public AbilityTargetType TargetType;
    public int             Damage;
    public int             Healing;
    public int             Shielding;
    public int             BaseCooldown;
    public int             CurrentCooldown;
    public int             BaseSigCharge;          // flat sig charge granted on use, regardless of effect values
    public int             Knockback;              // tiles displaced; positive = push away, negative = pull toward
    public bool            MovesUser;              // caster teleports to the anchor tile after ability resolves
    public bool            SwapWithTarget;         // caster and target exchange positions after ability resolves
    public int             RepositionRange;        // > 0: requires a second click to place the target within this range
    public System.Collections.Generic.List<AbilityStatusEffect>  StatusEffectsToApply  = new();
    public System.Collections.Generic.List<AbilityInstantEffect> InstantEffectsToApply = new();
    public AbilityTileEffect TileEffectToPlace; // null if ability places no tile effect

    public bool IsOnCooldown => CurrentCooldown > 0;

    public void SetCooldown()       => CurrentCooldown = BaseCooldown;
    public void TickCooldown()      => CurrentCooldown = UnityEngine.Mathf.Max(0, CurrentCooldown - 1);
    public void ReduceCooldown(int amount) => CurrentCooldown = UnityEngine.Mathf.Max(0, CurrentCooldown - amount);
}
