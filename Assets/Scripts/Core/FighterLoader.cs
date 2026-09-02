using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Reads fighters.json from StreamingAssets and maps data to game objects.
// Sprite loading uses Resources/fighters/ folder.
public static class FighterLoader
{
    private const string JsonFileName  = "fighters.json";
    private const string SpritePath    = "fighters/"; // relative to any Resources folder

    // ── Roster loading ─────────────────────────────────────────────────────

    public static Dictionary<string, FighterData> LoadRoster()
    {
        string path = Path.Combine(Application.streamingAssetsPath, JsonFileName);
        if (!File.Exists(path))
        {
            Debug.LogError($"[FighterLoader] {JsonFileName} not found at: {path}");
            return new Dictionary<string, FighterData>();
        }

        string json   = File.ReadAllText(path);
        var    roster = JsonUtility.FromJson<FighterRoster>(json);

        var dict = new Dictionary<string, FighterData>(StringComparer.OrdinalIgnoreCase);
        if (roster?.fighters != null)
            foreach (var f in roster.fighters)
                dict[f.name] = f;

        Debug.Log($"[FighterLoader] Loaded {dict.Count} fighters from roster.");
        return dict;
    }

    // ── Sprite loading ─────────────────────────────────────────────────────

    public static Sprite LoadSprite(string imageName)
    {
        if (string.IsNullOrEmpty(imageName)) return null;

        var sprite = Resources.Load<Sprite>(SpritePath + imageName);
        if (sprite == null)
            Debug.LogWarning($"[FighterLoader] Sprite not found: Resources/{SpritePath}{imageName}");
        return sprite;
    }

    // ── Ability building ───────────────────────────────────────────────────

    // Maps one FighterMoveData → Ability. Passives are built as display-only (no targeting data).
    // Every entry in move.effects becomes its own AbilityEffect — see BuildEffect.
    public static Ability BuildAbility(FighterMoveData move)
    {
        // "Signature" in JSON doesn't match the enum name "Sig" — handle manually
        AbilitySlot slot;
        if (string.Equals(move.type, "Signature", StringComparison.OrdinalIgnoreCase))
            slot = AbilitySlot.Sig;
        else if (!Enum.TryParse(move.type, true, out slot))
            slot = AbilitySlot.Normal;

        if (!Enum.TryParse(move.essence, true, out AbilityEssence essence))
            essence = AbilityEssence.None;

        // Passives: build display-only ability with no targeting data
        if (slot == AbilitySlot.Passive)
        {
            return new Ability
            {
                Name          = move.name,
                Description   = move.mechanics,
                Slot          = AbilitySlot.Passive,
                Essence       = essence,
                BaseCooldown  = 0,
                BaseSigCharge = 0,
            };
        }

        if (move.effects == null || move.effects.Length == 0)
            return null;

        var ability = new Ability
        {
            Name            = move.name,
            Description     = move.mechanics,
            Slot            = slot,
            Essence         = essence,
            BaseCooldown    = move.cooldown,
            BaseSigCharge   = move.baseSigCharge,
            Knockback       = move.knockback,
            MovesUser       = move.movesUser,
            SwapWithTarget  = move.swapWithTarget,
            RepositionRange = move.repositionRange,
        };

        foreach (var effectData in move.effects)
            ability.Effects.Add(BuildEffect(effectData));

        return ability;
    }

    // Maps one FighterEffectData entry (one item in a move's "effects" JSON array) → AbilityEffect.
    private static AbilityEffect BuildEffect(FighterEffectData effectData)
    {
        if (!Enum.TryParse(effectData.shape,      true, out AbilityShape      shape))      shape      = AbilityShape.Single;
        if (!Enum.TryParse(effectData.targetType, true, out AbilityTargetType targetType)) targetType = AbilityTargetType.Enemy;

        var effect = new AbilityEffect
        {
            TargetType              = targetType,
            Shape                   = shape,
            Range                   = effectData.range,
            MinRange                = effectData.minRange,
            Damage                  = effectData.damage,
            Healing                 = effectData.healing,
            Shielding               = effectData.shielding,
            RequiresSecondaryTarget = effectData.requiresSecondaryTarget,
            // 0/absent in JSON means "not specified" (JsonUtility's int default), not "zero
            // targets" — default it to 1 (today's normal single-target behavior) rather than
            // passing the raw 0 through.
            MaxTargets              = effectData.maxTargets > 0 ? effectData.maxTargets : 1,
        };

        // Box: parse "WxH" (e.g. "2x3") — all other shapes: parse plain int (e.g. "3")
        if (shape == AbilityShape.Box)
        {
            var parts = (effectData.shapeSize ?? "1x1").Split('x');
            effect.ShapeWidth  = parts.Length >= 1 && int.TryParse(parts[0], out int w) ? w : 1;
            effect.ShapeHeight = parts.Length >= 2 && int.TryParse(parts[1], out int h) ? h : 1;
        }
        else
        {
            effect.ShapeSize = int.TryParse(effectData.shapeSize, out int s) ? s : 1;
        }

        // Instant effects
        if (effectData.instantEffects != null)
        {
            foreach (var ie in effectData.instantEffects)
            {
                if (!Enum.TryParse(ie.type, true, out InstantEffectType ieType)) continue;

                effect.InstantEffectsToApply.Add(new AbilityInstantEffect
                {
                    Type        = ieType,
                    Magnitude   = ie.magnitude,
                    ApplyChance = ie.applyChance <= 0f ? 1f : ie.applyChance,
                });
            }
        }

        // Status effects
        if (effectData.statusEffects != null)
        {
            foreach (var se in effectData.statusEffects)
            {
                if (!Enum.TryParse(se.type, true, out StatusEffectType seType)) continue;

                effect.StatusEffectsToApply.Add(new AbilityStatusEffect
                {
                    Name        = se.name,
                    Type        = seType,
                    Essence     = se.essence,
                    Magnitude   = se.magnitude,
                    Duration    = se.duration,
                    IsDebuff    = se.isDebuff,
                    ApplyChance = se.applyChance <= 0f ? 1f : se.applyChance,
                    Condition   = BuildCondition(se.condition),
                });
            }
        }

        // Dynamic value — guard with valueType check since JsonUtility never returns null for class fields
        if (effectData.dynamicValue != null && !string.IsNullOrEmpty(effectData.dynamicValue.valueType))
            effect.DynamicValue = BuildDynamicValue(effectData.dynamicValue);

        // Tile effect — JsonUtility never returns null for class fields; guard with name check
        if (effectData.tileEffect != null && !string.IsNullOrEmpty(effectData.tileEffect.name))
        {
            var td = effectData.tileEffect;

            // JSON uses short names ("TurnEnd", "OnEnter") — try with prefix fallback
            string triggerStr = td.triggerOn ?? string.Empty;
            if (!Enum.TryParse(triggerStr, true, out TileEffectTrigger trigger) &&
                !Enum.TryParse("On" + triggerStr, true, out trigger))
                trigger = TileEffectTrigger.OnTurnEnd;

            // JSON uses "Ally"/"Enemy"/"All" — map to enum names
            TileEffectAffinity affinity = td.targetType switch
            {
                "Ally"   => TileEffectAffinity.AllyOnly,
                "Enemy"  => TileEffectAffinity.EnemyOnly,
                _        => TileEffectAffinity.All,
            };

            var tileEffect = new AbilityTileEffect
            {
                Name                   = td.name,
                Duration               = td.duration,
                Trigger                = trigger,
                Affinity               = affinity,
                Damage                 = td.damage,
                Healing                = td.healing,
                Shielding              = td.shielding,
                DestroyOnTrigger       = td.destroyOnTrigger,
                ExcludedSpecies        = td.excludedSpecies,
                RemoveRandomBuffChance = td.removeRandomBuffChance,
            };

            if (td.statusEffects != null)
            {
                foreach (var se in td.statusEffects)
                {
                    if (!Enum.TryParse(se.type, true, out StatusEffectType seType)) continue;
                    tileEffect.StatusEffectsToApply.Add(new AbilityStatusEffect
                    {
                        Name        = se.name,
                        Type        = seType,
                        Essence     = se.essence,
                        Magnitude   = se.magnitude,
                        Duration    = se.duration,
                        IsDebuff    = se.isDebuff,
                        ApplyChance = se.applyChance <= 0f ? 1f : se.applyChance,
                        Condition   = BuildCondition(se.condition),
                    });
                }
            }

            if (td.dynamicValue != null && !string.IsNullOrEmpty(td.dynamicValue.valueType))
                tileEffect.DynamicValue = BuildDynamicValue(td.dynamicValue);

            effect.TileEffectToPlace = tileEffect;
        }

        return effect;
    }

    private static EffectCondition BuildCondition(FighterConditionData data)
    {
        if (data == null || string.IsNullOrEmpty(data.source)) return null;
        if (!Enum.TryParse(data.source, true, out DynamicValueSource source)) return null;

        return new EffectCondition
        {
            Source     = source,
            StatusName = data.statusName,
            MinCount   = data.minCount,
        };
    }

    private static DynamicValue BuildDynamicValue(FighterDynamicValueData data)
    {
        if (!Enum.TryParse(data.valueType, true, out DynamicValueType valueType)) valueType = DynamicValueType.Damage;
        if (!Enum.TryParse(data.source,    true, out DynamicValueSource source))  source    = DynamicValueSource.CasterBuffs;

        return new DynamicValue
        {
            ValueType      = valueType,
            Source         = source,
            StatusName     = data.statusName,
            AmountPerStack = data.amountPerStack,
            IsConsumed     = data.isConsumed,
        };
    }
}
