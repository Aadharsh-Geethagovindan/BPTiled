using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class Fighter : MonoBehaviour
{
    [Header("Identity")]
    public string FighterName { get; private set; }
    public int TeamId { get; private set; }
    public string Species { get; private set; } = "";

    // All mutable game data lives here — see FighterState for why. Properties below are thin
    // wrappers over it so the rest of the codebase's call sites (fighter.CurrentHP etc.) don't
    // need to change.
    public FighterState State { get; private set; } = new FighterState();

    [Header("Stats")]
    public int   MaxHP            => State.MaxHP;
    public int   CurrentHP        => State.CurrentHP;
    public float Speed            => State.Speed;
    public int   SigChargeReq     => State.SigChargeReq;
    public float DamageMultiplier => State.DamageMultiplier;
    public float Accuracy         => State.Accuracy;
    public float DodgeChance      => State.DodgeChance;
    public float CritRate         => State.CritRate;
    public float CritDmg          => State.CritDmg;
    public int   Shield           => State.Shield;
    public int   CurrentCharge    => State.CurrentCharge;
    private void SetCharge(int value) { State.CurrentCharge = Mathf.Clamp(value, 0, State.SigChargeReq); OnChargeChanged?.Invoke(this); }

    [Header("Resistances")]
    public float ResArcane    => State.ResArcane;
    public float ResElemental => State.ResElemental;
    public float ResForce     => State.ResForce;
    public float ResCorrupt   => State.ResCorrupt;

    [Header("Essence Damage Bonuses")]
    public float BonusArcaneDmg    => State.BonusArcaneDmg;
    public float BonusElementalDmg => State.BonusElementalDmg;
    public float BonusForceDmg     => State.BonusForceDmg;
    public float BonusCorruptDmg   => State.BonusCorruptDmg;

    [Header("State")]
    public bool  HasActedThisTurn      => State.HasActedThisTurn;
    public bool  HasMovedThisTurn      => State.HasMovedThisTurn;
    public bool  HasActivatedThisRound => State.HasActivatedThisRound;
    public bool  IsDead                => State.IsDead;
    public float RemainingMovePoints   => State.RemainingMovePoints;

    // True for the whole duration of a stepped move — see FighterState.IsMoving.
    public bool IsMoving => State.IsMoving;
    public void SetMoving(bool value) => State.IsMoving = value;

    // Set true by a passive ability — allows movement after acting this turn
    public bool CanMoveAfterAction    => State.CanMoveAfterAction;

    // Cumulative tiles moved across all turns — used by Leyline Flow passive
    public int TotalTilesMoved => State.TotalTilesMoved;

    // Cumulative HP damage dealt across all turns — used by Constellian Trooper's passive
    public int TotalDamageDealt => State.TotalDamageDealt;

    [Header("Grid")]
    public Vector2Int GridPosition => State.GridPosition;

    [Header("Visual")]
    private SpriteRenderer _spriteRenderer;
    public  Sprite Portrait => _spriteRenderer != null ? _spriteRenderer.sprite : null;

    private Board _board;

    // Fired when HP or charge changes — UI panels subscribe to refresh displays
    public static event System.Action<Fighter> OnHPChanged;
    public static event System.Action<Fighter> OnChargeChanged;
    public static event System.Action<Fighter> OnStatusEffectsChanged;

    // Passive hooks — PassiveManager subscribes to these
    public static event System.Action<Fighter, int>          OnFighterDamaged;          // fighter, hp damage dealt
    public static event System.Action<Fighter>               OnFighterDied;
    public static event System.Action<Fighter, Fighter, StatusEffect> OnStatusEffectApplied; // caster, target, effect
    public static event System.Action<Fighter, int>          OnFighterMoved;            // fighter, tiles moved this step

    private readonly List<Ability> _abilities = new List<Ability>();
    public IReadOnlyList<Ability>      Abilities     => _abilities;
    public IReadOnlyList<StatusEffect> StatusEffects => State.StatusEffects;


    public void Initialize(string name, int teamId, int maxHP, float speed, int sigChargeReq,
                           float damageMultiplier, float accuracy, float dodgeChance,
                           float critRate, float critDmg,
                           float resArcane, float resElemental, float resForce, float resCorrupt,
                           Vector2Int startPosition, Board board)
    {
        FighterName = name;
        TeamId      = teamId;
        Species     = "";

        State = new FighterState
        {
            MaxHP             = maxHP,
            CurrentHP         = maxHP,
            Speed             = speed,
            SigChargeReq      = sigChargeReq,
            DamageMultiplier  = damageMultiplier,
            Accuracy          = accuracy,
            DodgeChance       = dodgeChance,
            CritRate          = critRate,
            CritDmg           = critDmg,
            Shield            = 0,
            CurrentCharge     = 0,
            ResArcane         = resArcane,
            ResElemental      = resElemental,
            ResForce          = resForce,
            ResCorrupt        = resCorrupt,
            RemainingMovePoints   = speed,
            GridPosition          = startPosition,
            HasActedThisTurn      = false,
            HasMovedThisTurn      = false,
            HasActivatedThisRound = false,
            IsDead                = false,
        };

        _board = board;

        _spriteRenderer = GetComponent<SpriteRenderer>();
        UpdateWorldPosition();
    }

    // animate = true tweens from the current world position to the new tile over one
    // MoveResolver.StepDurationMs beat instead of snapping — used for a single-tile hop that's
    // part of a stepped move (see SetGridPosition/ApplyNetworkState below), so the sprite visibly
    // crosses the tile rather than teleporting. Everything else (teleport/reposition abilities,
    // initial spawn, a full-state resync on joining) snaps instantly, unchanged from before.
    // This is a deliberately plain lerp — a placeholder for real walk-cycle sprite animation
    // later, which will replace this method's body without touching anything that calls it.
    private void UpdateWorldPosition(bool animate = false)
    {
        if (_board == null) return;
        var targetPos = _board.GridToWorld(GridPosition);

        if (animate && Application.isPlaying)
            AnimateStepTo(targetPos).Forget();
        else
            transform.position = targetPos;
    }

    // Generation counter guards against overlapping tweens if a new step starts before the
    // previous one's lerp finished — the stale loop just bails instead of fighting the new one
    // for transform.position. Not full cancellation machinery, just enough to stay correct.
    private int _moveAnimGen;

    private async UniTaskVoid AnimateStepTo(Vector3 targetPos)
    {
        int myGen = ++_moveAnimGen;
        Vector3 start = transform.position;
        float duration = MoveResolver.StepDurationMs / 1000f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (myGen != _moveAnimGen) return; // superseded by a newer step
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(start, targetPos, Mathf.Clamp01(elapsed / duration));
            await UniTask.Yield();
        }

        if (myGen == _moveAnimGen)
            transform.position = targetPos;
    }

    private static bool IsAdjacent(Vector2Int a, Vector2Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;

    // ── [SERVER ONLY] — called only from BattleController request methods ──

    public void SetGridPosition(Vector2Int newPosition)
    {
        bool animate = IsAdjacent(State.GridPosition, newPosition);
        State.GridPosition = newPosition;
        UpdateWorldPosition(animate);
    }

    // Returns actual HP damage dealt (after shield absorption, capped at remaining HP).
    // essence/source describe the incoming hit and feed the pre-damage interception checks below
    // (fighter-specific conditional immunity, status-driven redirect) — both run before shield/HP
    // are touched, so a fully-prevented or fully-redirected hit never reaches them. Existing call
    // sites that don't know/care about essence or source can omit them; interception simply won't
    // apply (no essence-conditional passive will match "True", and no redirect needs a source here).
    public int TakeDamage(int amount, string essence = "True", Fighter source = null)
    {
        if (amount > 0)
        {
            // Fighter-specific conditional immunity (e.g. Rei's Dark Empress). Hardcoded per-name
            // passive logic lives in PassiveManager, same as every other passive check.
            if (PassiveManager.Instance != null && PassiveManager.Instance.ShouldPreventDamage(this, essence, amount))
            {
                BattleLogger.Log($"{FighterName} is immune to this hit.", LogCategory.Passive);
                return 0;
            }

            // Generic status-driven redirect (e.g. JudgeWard) — reroutes a fraction of the hit to
            // whoever applied the status. Reuses the same SourceFighterName pattern TileEffect/
            // StatusEffect already use for sig-charge crediting.
            var redirect = State.StatusEffects.Find(e => e.Type == StatusEffectType.DamageRedirect);
            if (redirect != null && !string.IsNullOrEmpty(redirect.SourceFighterName))
            {
                var redirectTarget = FighterManager.Instance?.GetFighterByName(redirect.SourceFighterName);
                if (redirectTarget != null && redirectTarget != this && !redirectTarget.IsDead)
                {
                    int redirected = Mathf.RoundToInt(amount * redirect.Magnitude);
                    if (redirected > 0)
                    {
                        amount -= redirected;
                        redirectTarget.TakeDamage(redirected, essence);
                        BattleLogger.Log($"{redirect.Name}: {redirected} damage redirected from {FighterName} to {redirectTarget.FighterName}.", LogCategory.Effect);
                    }
                }
            }
        }

        bool shieldChanged = false;
        if (State.Shield > 0)
        {
            int absorbed = Mathf.Min(State.Shield, amount);
            State.Shield -= absorbed;
            amount -= absorbed;
            shieldChanged = absorbed > 0;
        }

        int hpDamage = 0;
        if (amount > 0)
        {
            hpDamage        = Mathf.Min(amount, State.CurrentHP);
            State.CurrentHP = Mathf.Max(0, State.CurrentHP - amount);
        }

        // Fires for a shield-only absorb too (hpDamage == 0), not just HP loss — otherwise a hit
        // fully blocked by shield would never tell the UI the shield bar needs to update.
        if (hpDamage > 0 || shieldChanged)
            OnHPChanged?.Invoke(this);

        if (hpDamage > 0)
            OnFighterDamaged?.Invoke(this, hpDamage);

        if (State.CurrentHP <= 0)
        {
            State.IsDead = true;
            gameObject.SetActive(false);
            OnFighterDied?.Invoke(this);
            BattleLogger.Log($"{FighterName} has been defeated.", LogCategory.Death);
        }

        return hpDamage;
    }

    // Returns actual HP restored (capped at missing HP)
    public int Heal(int amount)
    {
        int healed = Mathf.Min(amount, State.MaxHP - State.CurrentHP);
        State.CurrentHP += healed;
        OnHPChanged?.Invoke(this);
        return healed;
    }

    public void SetActed(bool value)     => State.HasActedThisTurn = value;
    public void SetMoved(bool value)     => State.HasMovedThisTurn = value;
    public void SetCanMoveAfterAction(bool value) => State.CanMoveAfterAction = value;
    public void SetSpecies(string species) => Species = species ?? "";
    public void SetTerrainCostMultiplier(float value) => State.TerrainCostMultiplier = value;
    public void SetTerrainCostThreshold(float value)  => State.TerrainCostThreshold  = value;

    // How much this fighter's own passives/state discount (or inflate) a tile's movement cost.
    // Threshold and multiplier are both plain per-fighter data (see FighterState) — this method
    // holds no fighter-specific assumptions itself, just the general rule for combining the two.
    public float GetEffectiveTerrainCost(Tile tile) =>
        tile.MovementCost > State.TerrainCostThreshold ? tile.MovementCost * State.TerrainCostMultiplier : tile.MovementCost;
    public void SetActivated(bool value) => State.HasActivatedThisRound = value;

    public void AddTilesMoved(int count)
    {
        State.TotalTilesMoved += count;
        OnFighterMoved?.Invoke(this, count);
    }

    public void AddDamageDealt(int amount) => State.TotalDamageDealt += amount;

    // ── Stat mutators (called from ability/passive resolvers) ───────────────

    // Returns amount actually added
    public int AddShield(int amount)
    {
        State.Shield += amount;
        OnHPChanged?.Invoke(this);
        return amount;
    }
    public void ModifyDamageMultiplier(float amt) => State.DamageMultiplier += amt;
    public void ModifyAccuracy(float amt)         => State.Accuracy = Mathf.Clamp(State.Accuracy + amt, 0f, 2f);
    public void ModifyDodge(float amt)            => State.DodgeChance = Mathf.Clamp(State.DodgeChance + amt, 0f, 1f);
    public void ModifyCritRate(float amt)         => State.CritRate = Mathf.Clamp(State.CritRate + amt, 0f, 1f);
    public void ModifyCritDmg(float amt)          => State.CritDmg = Mathf.Max(1f, State.CritDmg + amt);
    public void ModifyResistance(AbilityEssence essence, float amt)
    {
        switch (essence)
        {
            case AbilityEssence.Arcane:    State.ResArcane    += amt; break;
            case AbilityEssence.Elemental: State.ResElemental += amt; break;
            case AbilityEssence.Force:     State.ResForce     += amt; break;
            case AbilityEssence.Corrupt:   State.ResCorrupt   += amt; break;
        }
    }

    public void ModifyEssenceDmgBonus(AbilityEssence essence, float amt)
    {
        switch (essence)
        {
            case AbilityEssence.Arcane:    State.BonusArcaneDmg    += amt; break;
            case AbilityEssence.Elemental: State.BonusElementalDmg += amt; break;
            case AbilityEssence.Force:     State.BonusForceDmg     += amt; break;
            case AbilityEssence.Corrupt:   State.BonusCorruptDmg   += amt; break;
        }
    }

    // Returns the total essence-specific damage bonus for a given essence type.
    // Use in the damage formula: finalDmg = base * DamageMultiplier * (1 + GetEssenceDmgBonus(essence))
    public float GetEssenceDmgBonus(AbilityEssence essence) => essence switch
    {
        AbilityEssence.Arcane    => State.BonusArcaneDmg,
        AbilityEssence.Elemental => State.BonusElementalDmg,
        AbilityEssence.Force     => State.BonusForceDmg,
        AbilityEssence.Corrupt   => State.BonusCorruptDmg,
        _                        => 0f
    };

    public void IncreaseCharge(int amount)
    {
        State.CurrentCharge = Mathf.Min(State.CurrentCharge + amount, State.SigChargeReq);
        OnChargeChanged?.Invoke(this);
    }
    public void ResetCharge()
    {
        State.CurrentCharge = 0;
        OnChargeChanged?.Invoke(this);
    }

    // ── Stat readers — base + active status effect modifiers ──────────────

    public float GetModifiedAccuracy()
    {
        float total = State.Accuracy;
        foreach (var e in State.StatusEffects)
            if (e.Type == StatusEffectType.AccuracyModifier) total += e.Magnitude * e.Stacks;
        return Mathf.Max(0f, total);
    }

    public float GetModifiedDodge()
    {
        float total = State.DodgeChance;
        foreach (var e in State.StatusEffects)
            if (e.Type == StatusEffectType.DodgeModifier) total += e.Magnitude * e.Stacks;
        return Mathf.Clamp(total, 0f, 1f);
    }

    public float GetModifiedDamageMultiplier()
    {
        float total = State.DamageMultiplier;
        foreach (var e in State.StatusEffects)
            if (e.Type == StatusEffectType.DamageMultiplier) total += e.Magnitude * e.Stacks;
        return Mathf.Max(0f, total);
    }

    public float GetModifiedCritRate()
    {
        float total = State.CritRate;
        foreach (var e in State.StatusEffects)
            if (e.Type == StatusEffectType.CritRateModifier) total += e.Magnitude * e.Stacks;
        return Mathf.Clamp(total, 0f, 1f);
    }

    public float GetModifiedCritDmg()
    {
        float total = State.CritDmg;
        foreach (var e in State.StatusEffects)
            if (e.Type == StatusEffectType.CritDamageModifier) total += e.Magnitude * e.Stacks;
        return Mathf.Max(1f, total);
    }

    public float GetModifiedSpeed()
    {
        foreach (var e in State.StatusEffects)
            if (e.Type == StatusEffectType.Root) return 0f;

        float total = State.Speed;
        foreach (var e in State.StatusEffects)
            if (e.Type == StatusEffectType.SpeedModifier) total += e.Magnitude * e.Stacks;
        return Mathf.Max(0f, total);
    }

    public float GetModifiedResistance(AbilityEssence essence)
    {
        float total = essence switch
        {
            AbilityEssence.Arcane    => State.ResArcane,
            AbilityEssence.Elemental => State.ResElemental,
            AbilityEssence.Force     => State.ResForce,
            AbilityEssence.Corrupt   => State.ResCorrupt,
            _                        => 0f
        };
        foreach (var e in State.StatusEffects)
            if (e.Type == StatusEffectType.ResistanceModifier && e.Essence == essence.ToString())
                total += e.Magnitude * e.Stacks;
        return total;
    }

    // ── Status effects ─────────────────────────────────────────────────────

    // Applies a status effect. If one with the same Name already exists, increments stacks.
    // caster is optional — pass null for effects not originating from an ability (e.g. passive procs).
    public void ApplyStatusEffect(StatusEffect effect, Fighter caster = null)
    {
        // Fighter-specific immunity (e.g. Bessil's Nightmare's Grasp) — blocks the status entirely,
        // before stacking or events, same as PassiveManager.ShouldPreventDamage does for TakeDamage.
        if (PassiveManager.Instance != null && PassiveManager.Instance.IsImmuneToStatus(this, effect.Type))
        {
            BattleLogger.Log($"{FighterName} is immune to {effect.Name}.", LogCategory.Passive);
            return;
        }

        // Record who applied this so a later DoT/HoT tick can credit their charge (see
        // StatusEffect.GrantChargeToSource). Only overwrite when a caster is actually passed —
        // callers that intentionally omit caster (e.g. Hemorrhage, to avoid re-triggering
        // OnStatusEffectApplied) may have already set SourceFighterName manually beforehand.
        if (caster != null)
            effect.SourceFighterName = caster.FighterName;

        var existing = State.StatusEffects.Find(e => e.Name == effect.Name);
        if (existing != null)
            existing.Stacks++;
        else
            State.StatusEffects.Add(effect);

        // Speed buffs/debuffs take effect immediately on remaining move points this turn
        if (effect.Type == StatusEffectType.SpeedModifier)
            State.RemainingMovePoints = Mathf.Max(0f, State.RemainingMovePoints + effect.Magnitude);
        else if (effect.Type == StatusEffectType.Root)
            State.RemainingMovePoints = 0f; // matches GetModifiedSpeed() zeroing out — halts movement immediately, not just next turn

        OnStatusEffectsChanged?.Invoke(this);
        OnStatusEffectApplied?.Invoke(caster, this, effect);
    }

    // Removes all stacks of a named effect.
    public void RemoveStatusEffect(string name)
    {
        int removed = State.StatusEffects.RemoveAll(e => e.Name == name);
        if (removed > 0) OnStatusEffectsChanged?.Invoke(this);
    }

    // Removes one randomly-chosen status effect matching the given category (all its stacks, same
    // as RemoveStatusEffect). Used where "which one" isn't known ahead of time — e.g. Ulmika's
    // passive (a random debuff) or Trustless Engineer's Trappings (a random buff). Returns the
    // removed effect's name, or null if nothing qualified.
    public string RemoveRandomStatusEffect(bool isDebuff)
    {
        var candidates = State.StatusEffects.FindAll(e => e.IsDebuff == isDebuff);
        if (candidates.Count == 0) return null;

        var chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        State.StatusEffects.Remove(chosen);
        OnStatusEffectsChanged?.Invoke(this);
        return chosen.Name;
    }

    // Removes every buff (non-debuff) on this fighter and returns them, for a steal/transfer style
    // mechanic (e.g. Vemk Parlas's Sig). The returned objects keep their existing Duration/Stacks/
    // SourceFighterName as-is, so a receiving fighter gets them exactly as they were.
    public List<StatusEffect> RemoveAllBuffs()
    {
        var removed = State.StatusEffects.FindAll(e => !e.IsDebuff);
        if (removed.Count > 0)
        {
            State.StatusEffects.RemoveAll(e => !e.IsDebuff);
            OnStatusEffectsChanged?.Invoke(this);
        }
        return removed;
    }

    // Transient, server-only scratch space for carrying data between two separate resolution
    // passes of the same ability — e.g. Vemk Parlas's Sig captures a target's buffs on the primary
    // pass, then grants them to a chosen ally on the secondary pass once that target is picked.
    // Not part of FighterState: this never needs network sync, it only has to survive between two
    // calls on the authoritative peer, same machine, same Fighter instance.
    private List<StatusEffect> _pendingTransferredBuffs;

    public void StashTransferredBuffs(List<StatusEffect> buffs) => _pendingTransferredBuffs = buffs;

    public List<StatusEffect> TakeTransferredBuffs()
    {
        var buffs = _pendingTransferredBuffs ?? new List<StatusEffect>();
        _pendingTransferredBuffs = null;
        return buffs;
    }

    // ── Instant effects ────────────────────────────────────────────────────

    public void ModifyChargeFlat(int amount)
    {
        SetCharge(State.CurrentCharge + amount);
    }

    public void ModifyChargePercent(float fraction)
    {
        int delta = Mathf.RoundToInt(State.CurrentCharge * fraction);
        SetCharge(State.CurrentCharge + delta);
    }

    public void AddCooldownToSkills(int turns)
    {
        foreach (var ability in _abilities)
        {
            if (ability.Slot == AbilitySlot.Skill || ability.Slot == AbilitySlot.Skill2)
                ability.CurrentCooldown = Mathf.Max(0, ability.CurrentCooldown + turns);
        }
    }

    public void ResetSkillCooldowns()
    {
        foreach (var ability in _abilities)
        {
            if (ability.Slot == AbilitySlot.Skill || ability.Slot == AbilitySlot.Skill2)
                ability.CurrentCooldown = 0;
        }
    }

    // Called at START of fighter's turn.
    // DoT/HoT tick via Apply(); expired periodic effects are removed.
    public void TickPeriodicEffects()
    {
        bool changed = false;
        for (int i = State.StatusEffects.Count - 1; i >= 0; i--)
        {
            var e = State.StatusEffects[i];
            if (!e.IsPeriodic) continue;
            bool expired = e.Apply(this);
            if (expired) { State.StatusEffects.RemoveAt(i); changed = true; }
        }
        if (changed) OnStatusEffectsChanged?.Invoke(this);
    }

    // Called at END of fighter's turn.
    // All non-periodic effects decrement duration. Expired effects are removed.
    public void TickDurationEffects()
    {
        bool changed = false;
        for (int i = State.StatusEffects.Count - 1; i >= 0; i--)
        {
            var e = State.StatusEffects[i];
            if (e.Type == StatusEffectType.DamageOverTime || e.Type == StatusEffectType.HealOverTime) continue;

            e.Duration--;
            if (e.Duration <= 0) { State.StatusEffects.RemoveAt(i); changed = true; }
        }
        if (changed) OnStatusEffectsChanged?.Invoke(this);
    }

    // Subtract actual move cost from remaining pool; clamps at 0
    public void SubtractMovePoints(float cost)
    {
        State.RemainingMovePoints = Mathf.Max(0f, State.RemainingMovePoints - cost);
    }

    // Resets within-turn flags only — called at start of each fighter's activation
    public void ResetTurnState()
    {
        State.HasActedThisTurn    = false;
        State.HasMovedThisTurn    = false;
        State.RemainingMovePoints = GetModifiedSpeed();
    }

    // Resets per-round flag — called at round start
    public void ResetRoundState()
    {
        State.HasActivatedThisRound = false;
        ResetTurnState();
    }

    // ── Network apply — client-side state sync, no game logic re-run ──────

    // Wholesale-replaces State with a freshly deserialized copy from the server, then fires the
    // display-refresh events so existing UI panels redraw. Deliberately does NOT fire
    // OnFighterDamaged/OnStatusEffectApplied/OnFighterMoved — those exist solely to trigger
    // PassiveManager reactions, which must stay server-only (see PassiveManager.Initialize).
    // OnFighterDied does fire here, same as before — UI (death tint, portraits) needs it and
    // PassiveManager no longer listens for it on a pure client.
    public void ApplyNetworkState(FighterState newState, Board board)
    {
        bool wasDead = State.IsDead;

        // A one-tile delta means this sync is one step of a stepped move (see MoveResolver) —
        // animate it the same as the local/server path does. Anything else (teleport/reposition,
        // a multi-tile jump from a dropped update, a full resync on joining) snaps.
        bool animate = !newState.IsDead && IsAdjacent(State.GridPosition, newState.GridPosition);

        if (!newState.IsDead && newState.GridPosition != State.GridPosition)
        {
            var fromTile = board.GetTile(State.GridPosition);
            if (fromTile != null && fromTile.OccupyingCharacter == gameObject) fromTile.OccupyingCharacter = null;
            var toTile = board.GetTile(newState.GridPosition);
            if (toTile != null) toTile.OccupyingCharacter = gameObject;
        }

        State = newState;
        UpdateWorldPosition(animate);

        for (int i = 0; i < _abilities.Count && i < State.AbilityCooldowns.Count; i++)
            _abilities[i].CurrentCooldown = State.AbilityCooldowns[i];

        OnHPChanged?.Invoke(this);
        OnChargeChanged?.Invoke(this);
        OnStatusEffectsChanged?.Invoke(this);

        if (!wasDead && State.IsDead)
        {
            gameObject.SetActive(false);
            OnFighterDied?.Invoke(this);
        }
    }

    public void AddAbility(Ability ability) => _abilities.Add(ability);

    public void SetSprite(Sprite sprite)
    {
        if (_spriteRenderer == null) return;
        _spriteRenderer.sprite = sprite;

        // Auto-scale to fit within 90% of one tile, regardless of sprite PPU
        if (sprite != null && _board != null)
        {
            float targetSize = _board.TileSize * 0.9f;
            float largest    = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            if (largest > 0f)
            {
                float s = targetSize / largest;
                transform.localScale = new Vector3(s, s, 1f);
            }
        }
    }

    public void SetColor(Color color)
    {
        if (_spriteRenderer != null)
            _spriteRenderer.color = color;
        else
            Debug.LogWarning("[Fighter] SpriteRenderer is null in SetColor");
    }
}
