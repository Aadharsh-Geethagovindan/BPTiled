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

    // Maps one FighterMoveData → Ability.
    // Passives are built as display-only (no targeting data).
    // Multi-effect moves: first effect drives the shape; future work can expand this.
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

        var effect = move.effects[0];

        if (!Enum.TryParse(effect.shape,      true, out AbilityShape      shape))      shape      = AbilityShape.Single;
        if (!Enum.TryParse(effect.targetType, true, out AbilityTargetType targetType)) targetType = AbilityTargetType.Enemy;

        var ability = new Ability
        {
            Name          = move.name,
            Description   = move.mechanics,
            Slot          = slot,
            Essence       = essence,
            Shape         = shape,
            Range         = effect.range,
            MinRange      = effect.minRange,
            TargetType    = targetType,
            Damage        = effect.damage,
            Healing       = effect.healing,
            Shielding     = effect.shielding,
            BaseCooldown    = move.cooldown,
            BaseSigCharge   = move.baseSigCharge,
            Knockback       = move.knockback,
            MovesUser       = move.movesUser,
            SwapWithTarget  = move.swapWithTarget,
            RepositionRange = move.repositionRange,
        };

        // Box: parse "WxH" (e.g. "2x3") — all other shapes: parse plain int (e.g. "3")
        if (shape == AbilityShape.Box)
        {
            var parts = (effect.shapeSize ?? "1x1").Split('x');
            ability.ShapeWidth  = parts.Length >= 1 && int.TryParse(parts[0], out int w) ? w : 1;
            ability.ShapeHeight = parts.Length >= 2 && int.TryParse(parts[1], out int h) ? h : 1;
        }
        else
        {
            ability.ShapeSize = int.TryParse(effect.shapeSize, out int s) ? s : 1;
        }

        // Instant effects
        if (effect.instantEffects != null)
        {
            foreach (var ie in effect.instantEffects)
            {
                if (!Enum.TryParse(ie.type, true, out InstantEffectType ieType)) continue;

                ability.InstantEffectsToApply.Add(new AbilityInstantEffect
                {
                    Type        = ieType,
                    Magnitude   = ie.magnitude,
                    ApplyChance = ie.applyChance <= 0f ? 1f : ie.applyChance,
                });
            }
        }

        // Status effects
        if (effect.statusEffects != null)
        {
            foreach (var se in effect.statusEffects)
            {
                if (!Enum.TryParse(se.type, true, out StatusEffectType seType)) continue;

                ability.StatusEffectsToApply.Add(new AbilityStatusEffect
                {
                    Name        = se.name,
                    Type        = seType,
                    Essence     = se.essence,
                    Magnitude   = se.magnitude,
                    Duration    = se.duration,
                    IsDebuff    = se.isDebuff,
                    ApplyChance = se.applyChance <= 0f ? 1f : se.applyChance,
                });
            }
        }

        // Tile effect — JsonUtility never returns null for class fields; guard with name check
        if (effect.tileEffect != null && !string.IsNullOrEmpty(effect.tileEffect.name))
        {
            var td = effect.tileEffect;

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
                Name             = td.name,
                Duration         = td.duration,
                Trigger          = trigger,
                Affinity         = affinity,
                Damage           = td.damage,
                Healing          = td.healing,
                Shielding        = td.shielding,
                DestroyOnTrigger = td.destroyOnTrigger,
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
                    });
                }
            }

            ability.TileEffectToPlace = tileEffect;
        }

        return ability;
    }
}
