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
        // authoritative peer (hotseat, or the online server) should actually run it.
        // NOTE: this means tile effects are not currently visible to a pure client at all
        // (nothing broadcasts placements to observers yet) — GetEffectsAt/HasEffects will read
        // empty on that peer until a proper sync RPC is added.
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

    public void PlaceEffect(Vector2Int pos, AbilityTileEffect data, int sourceTeam)
    {
        if (!_effects.ContainsKey(pos))
            _effects[pos] = new List<TileEffect>();

        _effects[pos].Add(new TileEffect(data, sourceTeam));
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

    // ── Trigger: OnEnter (called from MoveResolver each tile step) ─────────

    public void HandleFighterEntered(Fighter fighter, Vector2Int pos)
    {
        if (!_effects.TryGetValue(pos, out var list) || list.Count == 0) return;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            var effect = list[i];
            if (effect.Trigger != TileEffectTrigger.OnEnter &&
                effect.Trigger != TileEffectTrigger.OnEnterDestroy) continue;
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
            if (effect.Trigger != TileEffectTrigger.OnTurnEnd) continue;
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

            // Apply each status effect for 1 turn with ToDisplay = false
            foreach (var se in effect.StatusEffectsToApply)
            {
                var status = new StatusEffect(se.Name, se.Type, se.Essence,
                                              se.Magnitude, 1, se.IsDebuff, toDisplay: false);
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
        if (effect.Damage > 0)
        {
            fighter.TakeDamage(effect.Damage);
            BattleLogger.Log($"{fighter.FighterName} took {effect.Damage} damage from {effect.Name}.", LogCategory.Hit);
        }

        if (effect.Healing > 0)
        {
            fighter.Heal(effect.Healing);
            BattleLogger.Log($"{fighter.FighterName} healed {effect.Healing} HP from {effect.Name}.", LogCategory.Hit);
        }

        if (effect.Shielding > 0)
        {
            fighter.AddShield(effect.Shielding);
            BattleLogger.Log($"{fighter.FighterName} gained {effect.Shielding} shield from {effect.Name}.", LogCategory.Hit);
        }

        foreach (var se in effect.StatusEffectsToApply)
        {
            var status = new StatusEffect(se.Name, se.Type, se.Essence,
                                          se.Magnitude, se.Duration, se.IsDebuff);
            fighter.ApplyStatusEffect(status);
            BattleLogger.Log($"{se.Name} applied to {fighter.FighterName} from {effect.Name}.", LogCategory.Effect);
        }
    }
}
