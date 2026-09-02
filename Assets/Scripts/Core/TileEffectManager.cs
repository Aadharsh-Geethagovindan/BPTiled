using System.Collections.Generic;
using UnityEngine;

// Owns all active tile effects on the board.
// Handles placement, trigger dispatch, and duration ticking.
public class TileEffectManager : MonoBehaviour
{
    public static TileEffectManager Instance { get; private set; }

    // Fired when tile effects on any position change — TileInfoPanel subscribes.
    public static event System.Action<Vector2Int> OnTileEffectsChanged;

    private readonly Dictionary<Vector2Int, List<TileEffect>> _effects = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Initialize()
    {
        // Same reasoning as PassiveManager: this mutates Fighter state directly in reaction to
        // events that also fire on pure clients as local mirrors of server state. Only the
        // authoritative peer (hotseat, or the online server) should actually run it — clients
        // instead receive the current effects wholesale via ApplyNetworkSnapshot, same pattern
        // as Fighter/FighterState (see BattleNetworkBridge.BroadcastBattleState).
        if (MatchSetup.Mode == GameMode.Online && !FishNet.InstanceFinder.IsServerStarted)
            return;

        TurnManager.OnFighterActivated += OnTurnStart;
        TurnManager.OnFighterTurnEnded += OnTurnEnd;
        TurnManager.OnRoundEnded       += OnRoundEnded;
    }

    private void OnDestroy()
    {
        TurnManager.OnFighterActivated -= OnTurnStart;
        TurnManager.OnFighterTurnEnded -= OnTurnEnd;
        TurnManager.OnRoundEnded       -= OnRoundEnded;
    }

    // ── Placement ──────────────────────────────────────────────────────────

    public void PlaceEffect(Vector2Int pos, AbilityTileEffect data, Fighter source)
    {
        if (!_effects.ContainsKey(pos))
            _effects[pos] = new List<TileEffect>();

        _effects[pos].Add(new TileEffect(data, source));
        OnTileEffectsChanged?.Invoke(pos);
        BattleLogger.Log($"{data.Name} placed at {pos} ({data.Duration} rounds).", LogCategory.Effect);
    }

    // ── Queries ────────────────────────────────────────────────────────────

    public IReadOnlyList<TileEffect> GetEffectsAt(Vector2Int pos)
    {
        return _effects.TryGetValue(pos, out var list) ? list : System.Array.Empty<TileEffect>();
    }

    public bool HasEffects(Vector2Int pos) =>
        _effects.TryGetValue(pos, out var list) && list.Count > 0;

    // ── Network sync (server captures, client applies wholesale) ──────────

    // Flattens the dictionary into a list for JsonUtility — see TileEffectSnapshotEntry.
    public List<TileEffectSnapshotEntry> CaptureSnapshot()
    {
        var list = new List<TileEffectSnapshotEntry>();
        foreach (var kvp in _effects)
            foreach (var effect in kvp.Value)
                list.Add(new TileEffectSnapshotEntry { X = kvp.Key.x, Y = kvp.Key.y, Effect = effect });
        return list;
    }

    // Replaces all current effects with the received snapshot, then notifies any position that
    // had effects before or has effects now (covers both new/updated tiles and tiles that lost
    // their last effect).
    public void ApplyNetworkSnapshot(List<TileEffectSnapshotEntry> snapshot)
    {
        var changedPositions = new HashSet<Vector2Int>(_effects.Keys);
        _effects.Clear();

        foreach (var entry in snapshot)
        {
            var pos = new Vector2Int(entry.X, entry.Y);
            if (!_effects.TryGetValue(pos, out var list))
            {
                list = new List<TileEffect>();
                _effects[pos] = list;
            }
            list.Add(entry.Effect);
            changedPositions.Add(pos);
        }

        foreach (var pos in changedPositions)
            OnTileEffectsChanged?.Invoke(pos);
    }

    // ── Trigger: OnEnter (called from MoveResolver each tile step) ─────────

    public void HandleFighterEntered(Fighter fighter, Vector2Int pos)
    {
        if (!_effects.TryGetValue(pos, out var list) || list.Count == 0) return;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            var effect = list[i];
            if (effect.Trigger != TileEffectTrigger.OnEnter &&
                effect.Trigger != TileEffectTrigger.OnEnterDestroy &&
                effect.Trigger != TileEffectTrigger.OnEnterOrTurnEnd) continue;
            if (!effect.AffectsFighter(fighter)) continue;

            ApplyEffectToFighter(fighter, effect);

            if (effect.DestroyOnTrigger || effect.Trigger == TileEffectTrigger.OnEnterDestroy)
            {
                list.RemoveAt(i);
                if (list.Count == 0) _effects.Remove(pos);
                OnTileEffectsChanged?.Invoke(pos);
            }
        }
    }

    // ── Trigger: OnTurnEnd (called from TurnManager) ───────────────────────

    private void OnTurnEnd(Fighter fighter)
    {
        var pos = fighter.GridPosition;
        if (!_effects.TryGetValue(pos, out var list) || list.Count == 0) return;

        foreach (var effect in list)
        {
            if (effect.Trigger != TileEffectTrigger.OnTurnEnd &&
                effect.Trigger != TileEffectTrigger.OnEnterOrTurnEnd) continue;
            if (!effect.AffectsFighter(fighter)) continue;
            ApplyEffectToFighter(fighter, effect);
        }
    }

    // ── Trigger: Persistent (called at turn START — applies hidden 1-turn status) ──

    private void OnTurnStart(Fighter fighter)
    {
        var pos = fighter.GridPosition;
        if (!_effects.TryGetValue(pos, out var list) || list.Count == 0) return;

        foreach (var effect in list)
        {
            if (effect.Trigger != TileEffectTrigger.Persistent) continue;
            if (!effect.AffectsFighter(fighter)) continue;

            var tileSource = !string.IsNullOrEmpty(effect.SourceFighterName) ? FighterManager.Instance?.GetFighterByName(effect.SourceFighterName) : null;

            // Apply each status effect for 1 turn with ToDisplay = false
            foreach (var se in effect.StatusEffectsToApply)
            {
                if (!DynamicValueResolver.ConditionMet(se.Condition, tileSource, fighter)) continue;
                var status = new StatusEffect(se.Name, se.Type, se.Essence,
                                              se.Magnitude, 1, se.IsDebuff, toDisplay: false);
                status.SourceFighterName = effect.SourceFighterName;
                fighter.ApplyStatusEffect(status);
            }
        }
    }

    // ── Duration ticking (at round end) ───────────────────────────────────

    private void OnRoundEnded(int _roundNumber)
    {
        var toRemove = new List<Vector2Int>();

        foreach (var kvp in _effects)
        {
            var list = kvp.Value;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                list[i].Duration--;
                if (list[i].Duration <= 0)
                {
                    BattleLogger.Log($"{list[i].Name} expired at {kvp.Key}.", LogCategory.Effect);
                    list.RemoveAt(i);
                }
            }
            if (list.Count == 0) toRemove.Add(kvp.Key);
            else OnTileEffectsChanged?.Invoke(kvp.Key);
        }

        foreach (var pos in toRemove)
        {
            _effects.Remove(pos);
            OnTileEffectsChanged?.Invoke(pos);
        }
    }

    // ── Effect application ─────────────────────────────────────────────────

    private static void ApplyEffectToFighter(Fighter fighter, TileEffect effect)
    {
        int chargeEarned = 0;

        var tileSource = !string.IsNullOrEmpty(effect.SourceFighterName) ? FighterManager.Instance?.GetFighterByName(effect.SourceFighterName) : null;

        // Dynamic bonus (e.g. Bessil's DarkShadow scaling with the triggering fighter's debuffs) —
        // caster is whoever placed the tile, target is the fighter that triggered it.
        int dynamicDamage = 0, dynamicHealing = 0, dynamicShielding = 0;
        if (effect.DynamicValue != null)
        {
            int bonus = DynamicValueResolver.ComputeBonus(effect.DynamicValue, tileSource, fighter);
            switch (effect.DynamicValue.ValueType)
            {
                case DynamicValueType.Damage:    dynamicDamage    = bonus; break;
                case DynamicValueType.Healing:   dynamicHealing   = bonus; break;
                case DynamicValueType.Shielding: dynamicShielding = bonus; break;
            }
        }

        if (effect.Damage > 0 || dynamicDamage > 0)
        {
            int dealt = fighter.TakeDamage(effect.Damage + dynamicDamage, "True", tileSource);
            chargeEarned += Mathf.RoundToInt(dealt * AbilityResolver.DamageChargeWeight);
            BattleLogger.Log($"{fighter.FighterName} took {dealt} damage from {effect.Name}.", LogCategory.Hit);
        }

        if (effect.Healing > 0 || dynamicHealing > 0)
        {
            int healed = fighter.Heal(effect.Healing + dynamicHealing);
            chargeEarned += Mathf.RoundToInt(healed * AbilityResolver.HealingChargeWeight);
            BattleLogger.Log($"{fighter.FighterName} healed {healed} HP from {effect.Name}.", LogCategory.Hit);
        }

        if (effect.Shielding > 0 || dynamicShielding > 0)
        {
            int shielded = fighter.AddShield(effect.Shielding + dynamicShielding);
            chargeEarned += Mathf.RoundToInt(shielded * AbilityResolver.ShieldChargeWeight);
            BattleLogger.Log($"{fighter.FighterName} gained {shielded} shield from {effect.Name}.", LogCategory.Hit);
        }

        if (effect.DynamicValue != null && tileSource != null)
            DynamicValueResolver.Consume(effect.DynamicValue, tileSource);

        if (effect.RemoveRandomBuffChance > 0f && Random.value <= effect.RemoveRandomBuffChance)
        {
            var removed = fighter.RemoveRandomStatusEffect(isDebuff: false);
            if (removed != null)
                BattleLogger.Log($"{fighter.FighterName} lost {removed} from {effect.Name}.", LogCategory.Effect);
        }

        foreach (var se in effect.StatusEffectsToApply)
        {
            if (!DynamicValueResolver.ConditionMet(se.Condition, tileSource, fighter)) continue;
            var status = new StatusEffect(se.Name, se.Type, se.Essence,
                                          se.Magnitude, se.Duration, se.IsDebuff);
            status.SourceFighterName = effect.SourceFighterName;
            fighter.ApplyStatusEffect(status);
            BattleLogger.Log($"{se.Name} applied to {fighter.FighterName} from {effect.Name}.", LogCategory.Effect);
        }

        // Grant charge to whoever placed this effect — same weights as a direct hit, so a
        // fighter earns charge consistently whether the damage/heal/shield came from their own
        // attack or from a zone they placed going off later. No-op if the placer has since died.
        if (chargeEarned > 0 && !string.IsNullOrEmpty(effect.SourceFighterName))
        {
            var source = FighterManager.Instance?.GetFighterByName(effect.SourceFighterName);
            if (source != null && !source.IsDead)
                source.IncreaseCharge(chargeEarned);
        }
    }
}
