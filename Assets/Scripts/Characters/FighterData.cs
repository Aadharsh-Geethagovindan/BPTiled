using System;

// Plain data classes for deserializing fighters.json.
// Fields use camelCase to match JSON keys exactly (JsonUtility is case-sensitive).
// Only fields we currently consume are declared; unknown JSON fields are silently ignored.

[Serializable]
public class FighterResistanceData
{
    public float arcane;
    public float elemental;
    public float force;
    public float corrupt;
}

// Gates whether a status effect is applied at all, checked against pre-cast caster/target state
// before the applyChance roll. Reuses the same source/counting semantics as dynamicValue.
[Serializable]
public class FighterConditionData
{
    public string source;     // maps to DynamicValueSource ("CasterBuffs" | "CasterDebuffs" | "TargetBuffs" | "TargetDebuffs" | "NamedStatus")
    public string statusName; // only used when source == "NamedStatus"
    public int    minCount;   // minimum stack count required; <=0 treated as 1
}

[Serializable]
public class FighterStatusEffectData
{
    public string name;
    public string type;         // maps to StatusEffectType enum
    public string essence;
    public float  magnitude;
    public int    duration;
    public bool   isDebuff;
    public float  applyChance; // 0.0–1.0; 1.0 = always applies
    public FighterConditionData condition; // null = always eligible (subject to applyChance only)
}

[Serializable]
public class FighterInstantEffectData
{
    public string type;         // maps to InstantEffectType enum
    public float  magnitude;
    public float  applyChance; // 0.0–1.0; 1.0 = always applies
}

// Damage/healing/shielding that scales at resolution time off status-effect counts, instead of
// being a fixed JSON number. See DynamicValueResolver for interpretation.
[Serializable]
public class FighterDynamicValueData
{
    public string valueType;      // "Damage" | "Healing" | "Shielding"  (maps to DynamicValueType)
    public string source;         // "CasterBuffs" | "CasterDebuffs" | "TargetBuffs" | "TargetDebuffs" | "NamedStatus"
    public string statusName;     // only used when source == "NamedStatus"
    public float  amountPerStack;
    public bool   isConsumed;     // NamedStatus only: remove the named status after computing the bonus
}

[Serializable]
public class TileEffectData
{
    public string                    name;
    public int                       duration;
    public string                    targetType;   // "Ally" | "Enemy" | "All"  (maps to TileEffectAffinity)
    public string                    triggerOn;    // "TurnEnd" | "OnEnter" | "OnEnterDestroy" | "Persistent"
    public int                       damage;
    public int                       healing;
    public int                       shielding;
    public bool                      destroyOnTrigger;
    public FighterStatusEffectData[] statusEffects;
    public FighterDynamicValueData   dynamicValue;      // null if this tile effect's values are all fixed
    public string                    excludedSpecies;   // e.g. "Riftbeast" — fighters of this species are unaffected regardless of affinity
    public float                     removeRandomBuffChance; // 0–1; on trigger, this % chance to strip one random buff from the affected fighter
}

[Serializable]
public class FighterEffectData
{
    public string                    targetType;   // "Enemy" | "Ally" | "Tile" | "Self"
    public int                       damage;
    public int                       healing;
    public int                       shielding;
    public int                       range;
    public int                       minRange;
    public string                    shape;        // "Single" | "Line" | "Cone" | "Cross" | "Ring" | "Area"
    public string                    shapeSize;    // plain int for most shapes: "3" — Area uses WxH format: "2x3"
    public FighterStatusEffectData[]  statusEffects;
    public FighterInstantEffectData[] instantEffects;
    public TileEffectData             tileEffect;    // null if ability places no tile effect
    public FighterDynamicValueData    dynamicValue;   // null if this effect's values are all fixed
    public bool                       requiresSecondaryTarget; // true only if this effect needs its own independent target pick (see AbilityEffect.RequiresSecondaryTarget)
    public int                        maxTargets;   // > 1 = multi-select targeting (see AbilityEffect.MaxTargets); 0/absent means "not specified", loader defaults it to 1
}

[Serializable]
public class FighterMoveData
{
    public string              name;
    public string              flavor;         // lore/flavour text
    public string              mechanics;      // gameplay description shown in ability panel
    public string              type;           // "Passive" | "Normal" | "Skill" | "Sig"
    public string              essence;        // "None" | "Arcane" | "Elemental" | "Force" | "Corrupt" | "True"
    public int                 cooldown;          // base cooldown in rounds (0 = no cooldown)
    public int                 baseSigCharge;     // flat sig charge granted on use
    public int                 knockback;         // tiles displaced; positive = push away, negative = pull toward
    public bool                movesUser;         // caster teleports to the anchor tile after ability resolves
    public bool                swapWithTarget;    // caster and target swap positions on hit
    public int                 repositionRange;   // > 0: second click required to place hit target within this range
    public FighterEffectData[] effects;
}

[Serializable]
public class FighterData
{
    public string                name;
    public string                rarity;           // "L" | "UR" | "R" | "UC" | "C"
    public int                   hp;
    public float                 speed;
    public int                   sigChargeReq;
    public string                species;          // e.g. "Riftbeast" — used for species-conditional targeting (see TileEffect.ExcludedSpecies)
    public float                 damageMultiplier; // default 1.0 in code if 0 in JSON
    public float                 accuracy;         // default 1.0
    public float                 dodgeChance;      // default 0.0
    public float                 critRate;         // default 0.1
    public float                 critDmg;          // default 1.5
    public string                imageName;
    public FighterResistanceData resistances;
    public FighterMoveData[]     moves;
}

[Serializable]
public class FighterRoster
{
    public FighterData[] fighters;
}
