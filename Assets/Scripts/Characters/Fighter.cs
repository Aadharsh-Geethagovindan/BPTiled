using System.Collections.Generic;
using UnityEngine;

public class Fighter : MonoBehaviour
{
    [Header("Identity")]
    public string FighterName { get; private set; }
    public int TeamId { get; private set; }

    [Header("Stats")]
    public int   MaxHP            { get; private set; }
    public int   CurrentHP        { get; private set; }
    public float Speed            { get; private set; }
    public int   SigChargeReq     { get; private set; }
    public float DamageMultiplier { get; private set; }
    public float Accuracy         { get; private set; }
    public float DodgeChance      { get; private set; }
    public float CritRate         { get; private set; }
    public float CritDmg          { get; private set; }
    public int   Shield           { get; private set; }
    public int   CurrentCharge    { get; private set; }
    private void SetCharge(int value) { CurrentCharge = Mathf.Clamp(value, 0, SigChargeReq); OnChargeChanged?.Invoke(this); }

    [Header("Resistances")]
    public float ResArcane    { get; private set; }
    public float ResElemental { get; private set; }
    public float ResForce     { get; private set; }
    public float ResCorrupt   { get; private set; }

    [Header("Essence Damage Bonuses")]
    public float BonusArcaneDmg    { get; private set; }
    public float BonusElementalDmg { get; private set; }
    public float BonusForceDmg     { get; private set; }
    public float BonusCorruptDmg   { get; private set; }

    [Header("State")]
    public bool  HasActedThisTurn      { get; private set; }
    public bool  HasMovedThisTurn      { get; private set; }
    public bool  HasActivatedThisRound { get; private set; }
    public bool  IsDead                { get; private set; }
    public float RemainingMovePoints   { get; private set; }

    // Set true by a passive ability — allows movement after acting this turn
    public bool CanMoveAfterAction    { get; private set; }

    // Cumulative tiles moved across all turns — used by Leyline Flow passive
    public int TotalTilesMoved { get; private set; }

    [Header("Grid")]
    public Vector2Int GridPosition { get; private set; }

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

    private readonly List<Ability>       _abilities      = new List<Ability>();
    private readonly List<StatusEffect>  _statusEffects  = new List<StatusEffect>();
    public IReadOnlyList<Ability>       Abilities      => _abilities;
    public IReadOnlyList<StatusEffect>  StatusEffects  => _statusEffects;


    public void Initialize(string name, int teamId, int maxHP, float speed, int sigChargeReq,
                           float damageMultiplier, float accuracy, float dodgeChance,
                           float critRate, float critDmg,
                           float resArcane, float resElemental, float resForce, float resCorrupt,
                           Vector2Int startPosition, Board board)
    {
        FighterName       = name;
        TeamId            = teamId;
        MaxHP             = maxHP;
        CurrentHP         = maxHP;
        Speed             = speed;
        SigChargeReq      = sigChargeReq;
        DamageMultiplier  = damageMultiplier;
        Accuracy          = accuracy;
        DodgeChance       = dodgeChance;
        CritRate          = critRate;
        CritDmg           = critDmg;
        Shield            = 0;
        CurrentCharge     = 0;
        ResArcane         = resArcane;
        ResElemental      = resElemental;
        ResForce          = resForce;
        ResCorrupt        = resCorrupt;
        RemainingMovePoints   = speed;
        GridPosition          = startPosition;
        HasActedThisTurn      = false;
        HasMovedThisTurn      = false;
        HasActivatedThisRound = false;
        IsDead                = false;
        _board = board;

        _spriteRenderer = GetComponent<SpriteRenderer>();
        UpdateWorldPosition();
    }

   private void UpdateWorldPosition()
    {
        if (_board != null)
        {
            var pos = _board.GridToWorld(GridPosition);
            //Debug.Log($"[Fighter] UpdateWorldPosition setting pos: {pos}"); //NEW
            transform.position = pos;
            //Debug.Log($"[Fighter] Transform position after set: {transform.position}"); //NEW
        }
    }

    // ── [SERVER ONLY] — called only from BattleController request methods ──

    public void SetGridPosition(Vector2Int newPosition)
    {
        GridPosition = newPosition;
        UpdateWorldPosition();
    }

    // Returns actual HP damage dealt (after shield absorption, capped at remaining HP)
    public int TakeDamage(int amount)
    {
        if (Shield > 0)
        {
            int absorbed = Mathf.Min(Shield, amount);
            Shield -= absorbed;
            amount -= absorbed;
        }

        int hpDamage = 0;
        if (amount > 0)
        {
            hpDamage  = Mathf.Min(amount, CurrentHP);
            CurrentHP = Mathf.Max(0, CurrentHP - amount);
            OnHPChanged?.Invoke(this);
        }

        if (hpDamage > 0)
            OnFighterDamaged?.Invoke(this, hpDamage);

        if (CurrentHP <= 0)
        {
            IsDead = true;
            gameObject.SetActive(false);
            OnFighterDied?.Invoke(this);
            BattleLogger.Log($"{FighterName} has been defeated.", LogCategory.Death);
        }

        return hpDamage;
    }

    // Returns actual HP restored (capped at missing HP)
    public int Heal(int amount)
    {
        int healed = Mathf.Min(amount, MaxHP - CurrentHP);
        CurrentHP += healed;
        OnHPChanged?.Invoke(this);
        return healed;
    }

    public void SetActed(bool value)     => HasActedThisTurn = value;
    public void SetMoved(bool value)     => HasMovedThisTurn = value;
    public void SetActivated(bool value) => HasActivatedThisRound = value;

    public void AddTilesMoved(int count)
    {
        TotalTilesMoved += count;
        OnFighterMoved?.Invoke(this, count);
    }

    // ── Stat mutators (called from ability/passive resolvers) ───────────────

    // Returns amount actually added
    public int AddShield(int amount) { Shield += amount; return amount; }
    public void ModifyDamageMultiplier(float amt) => DamageMultiplier += amt;
    public void ModifyAccuracy(float amt)         => Accuracy = UnityEngine.Mathf.Clamp(Accuracy + amt, 0f, 2f);
    public void ModifyDodge(float amt)            => DodgeChance = UnityEngine.Mathf.Clamp(DodgeChance + amt, 0f, 1f);
    public void ModifyCritRate(float amt)         => CritRate = UnityEngine.Mathf.Clamp(CritRate + amt, 0f, 1f);
    public void ModifyCritDmg(float amt)          => CritDmg = UnityEngine.Mathf.Max(1f, CritDmg + amt);
    public void ModifyResistance(AbilityEssence essence, float amt)
    {
        switch (essence)
        {
            case AbilityEssence.Arcane:    ResArcane    += amt; break;
            case AbilityEssence.Elemental: ResElemental += amt; break;
            case AbilityEssence.Force:     ResForce     += amt; break;
            case AbilityEssence.Corrupt:   ResCorrupt   += amt; break;
        }
    }

    public void ModifyEssenceDmgBonus(AbilityEssence essence, float amt)
    {
        switch (essence)
        {
            case AbilityEssence.Arcane:    BonusArcaneDmg    += amt; break;
            case AbilityEssence.Elemental: BonusElementalDmg += amt; break;
            case AbilityEssence.Force:     BonusForceDmg     += amt; break;
            case AbilityEssence.Corrupt:   BonusCorruptDmg   += amt; break;
        }
    }

    // Returns the total essence-specific damage bonus for a given essence type.
    // Use in the damage formula: finalDmg = base * DamageMultiplier * (1 + GetEssenceDmgBonus(essence))
    public float GetEssenceDmgBonus(AbilityEssence essence) => essence switch
    {
        AbilityEssence.Arcane    => BonusArcaneDmg,
        AbilityEssence.Elemental => BonusElementalDmg,
        AbilityEssence.Force     => BonusForceDmg,
        AbilityEssence.Corrupt   => BonusCorruptDmg,
        _                        => 0f
    };

    public void IncreaseCharge(int amount)
    {
        CurrentCharge = UnityEngine.Mathf.Min(CurrentCharge + amount, SigChargeReq);
        OnChargeChanged?.Invoke(this);
    }
    public void ResetCharge()
    {
        CurrentCharge = 0;
        OnChargeChanged?.Invoke(this);
    }

    // ── Stat readers — base + active status effect modifiers ──────────────

    public float GetModifiedAccuracy()
    {
        float total = Accuracy;
        foreach (var e in _statusEffects)
            if (e.Type == StatusEffectType.AccuracyModifier) total += e.Magnitude * e.Stacks;
        return Mathf.Max(0f, total);
    }

    public float GetModifiedDodge()
    {
        float total = DodgeChance;
        foreach (var e in _statusEffects)
            if (e.Type == StatusEffectType.DodgeModifier) total += e.Magnitude * e.Stacks;
        return Mathf.Clamp(total, 0f, 1f);
    }

    public float GetModifiedDamageMultiplier()
    {
        float total = DamageMultiplier;
        foreach (var e in _statusEffects)
            if (e.Type == StatusEffectType.DamageMultiplier) total += e.Magnitude * e.Stacks;
        return Mathf.Max(0f, total);
    }

    public float GetModifiedCritRate()
    {
        float total = CritRate;
        foreach (var e in _statusEffects)
            if (e.Type == StatusEffectType.CritRateModifier) total += e.Magnitude * e.Stacks;
        return Mathf.Clamp(total, 0f, 1f);
    }

    public float GetModifiedCritDmg()
    {
        float total = CritDmg;
        foreach (var e in _statusEffects)
            if (e.Type == StatusEffectType.CritDamageModifier) total += e.Magnitude * e.Stacks;
        return Mathf.Max(1f, total);
    }

    public float GetModifiedSpeed()
    {
        foreach (var e in _statusEffects)
            if (e.Type == StatusEffectType.Root) return 0f;

        float total = Speed;
        foreach (var e in _statusEffects)
            if (e.Type == StatusEffectType.SpeedModifier) total += e.Magnitude * e.Stacks;
        return Mathf.Max(0f, total);
    }

    public float GetModifiedResistance(AbilityEssence essence)
    {
        float total = essence switch
        {
            AbilityEssence.Arcane    => ResArcane,
            AbilityEssence.Elemental => ResElemental,
            AbilityEssence.Force     => ResForce,
            AbilityEssence.Corrupt   => ResCorrupt,
            _                        => 0f
        };
        foreach (var e in _statusEffects)
            if (e.Type == StatusEffectType.ResistanceModifier) total += e.Magnitude * e.Stacks;
        return total;
    }

    // ── Status effects ─────────────────────────────────────────────────────

    // Applies a status effect. If one with the same Name already exists, increments stacks.
    // caster is optional — pass null for effects not originating from an ability (e.g. passive procs).
    public void ApplyStatusEffect(StatusEffect effect, Fighter caster = null)
    {
        var existing = _statusEffects.Find(e => e.Name == effect.Name);
        if (existing != null)
            existing.Stacks++;
        else
            _statusEffects.Add(effect);

        // Speed buffs/debuffs take effect immediately on remaining move points this turn
        if (effect.Type == StatusEffectType.SpeedModifier)
            RemainingMovePoints = Mathf.Max(0f, RemainingMovePoints + effect.Magnitude);

        OnStatusEffectsChanged?.Invoke(this);
        OnStatusEffectApplied?.Invoke(caster, this, effect);
    }

    // Removes all stacks of a named effect.
    public void RemoveStatusEffect(string name)
    {
        int removed = _statusEffects.RemoveAll(e => e.Name == name);
        if (removed > 0) OnStatusEffectsChanged?.Invoke(this);
    }

    // ── Instant effects ────────────────────────────────────────────────────

    public void ModifyChargeFlat(int amount)
    {
        SetCharge(CurrentCharge + amount);
    }

    public void ModifyChargePercent(float fraction)
    {
        int delta = Mathf.RoundToInt(CurrentCharge * fraction);
        SetCharge(CurrentCharge + delta);
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
        for (int i = _statusEffects.Count - 1; i >= 0; i--)
        {
            var e = _statusEffects[i];
            if (!e.IsPeriodic) continue;
            bool expired = e.Apply(this);
            if (expired) { _statusEffects.RemoveAt(i); changed = true; }
        }
        if (changed) OnStatusEffectsChanged?.Invoke(this);
    }

    // Called at END of fighter's turn.
    // All non-periodic effects decrement duration. Expired effects are removed.
    public void TickDurationEffects()
    {
        bool changed = false;
        for (int i = _statusEffects.Count - 1; i >= 0; i--)
        {
            var e = _statusEffects[i];
            if (e.Type == StatusEffectType.DamageOverTime || e.Type == StatusEffectType.HealOverTime) continue;

            e.Duration--;
            if (e.Duration <= 0) { _statusEffects.RemoveAt(i); changed = true; }
        }
        if (changed) OnStatusEffectsChanged?.Invoke(this);
    }

    // Subtract actual move cost from remaining pool; clamps at 0
    public void SubtractMovePoints(float cost)
    {
        RemainingMovePoints = Mathf.Max(0f, RemainingMovePoints - cost);
    }

    // Resets within-turn flags only — called at start of each fighter's activation
    public void ResetTurnState()
    {
        HasActedThisTurn    = false;
        HasMovedThisTurn    = false;
        RemainingMovePoints = GetModifiedSpeed();
    }

    // Resets per-round flag — called at round start
    public void ResetRoundState()
    {
        HasActivatedThisRound = false;
        ResetTurnState();
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