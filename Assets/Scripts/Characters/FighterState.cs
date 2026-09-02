using System.Collections.Generic;
using UnityEngine;

// All of a Fighter's mutable game data, in one place. This is the actual backing storage for
// Fighter's stat properties (Fighter.CurrentHP etc. are thin wrappers over State.currentHP) —
// not a separate copy that something has to remember to keep up to date.
//
// Network sync serializes this whole object (JsonUtility.ToJson(fighter.State)) rather than
// listing individual fields in an RPC signature. Anything added here is automatically included
// the next time it's serialized — there is no separate list of "fields to sync" to forget.
// Identity (FighterName, TeamId) and Unity references (SpriteRenderer, Board) deliberately stay
// off of Fighter itself, not in here, since they never change and don't need replicating.
[System.Serializable]
public class FighterState
{
    public int   MaxHP;
    public int   CurrentHP;
    public float Speed;
    public int   SigChargeReq;
    public float DamageMultiplier;
    public float Accuracy;
    public float DodgeChance;
    public float CritRate;
    public float CritDmg;
    public int   Shield;
    public int   CurrentCharge;

    public float ResArcane;
    public float ResElemental;
    public float ResForce;
    public float ResCorrupt;

    public float BonusArcaneDmg;
    public float BonusElementalDmg;
    public float BonusForceDmg;
    public float BonusCorruptDmg;

    public bool  HasActedThisTurn;
    public bool  HasMovedThisTurn;
    public bool  HasActivatedThisRound;
    public bool  IsDead;
    public float RemainingMovePoints;

    public bool CanMoveAfterAction;
    public int  TotalTilesMoved;

    // True for the whole duration of a stepped move (see MoveResolver/ProgressiveResolver), not
    // just the current tile-step — rides along on the same per-step wholesale broadcasts, so
    // remote clients gate their own input on it the same way the local UI does (SelectionManager
    // checks Fighter.IsMoving before accepting a new tile click/ability/move-mode request).
    public bool IsMoving;

    // Cumulative HP damage dealt across all turns — used by Constellian Trooper's passive. First
    // of what will likely become a small family of match-cumulative stat fields (healing dealt/
    // received, etc.) if/when a stats breakdown is built; only this one exists for now.
    public int TotalDamageDealt;

    // Terrain cost modifier (e.g. Sedra's Aetherian Momentum). Threshold default of 0 means "no
    // restriction" — the multiplier applies to any tile with cost > 0 unless a passive narrows it.
    public float TerrainCostMultiplier = 1f;
    public float TerrainCostThreshold  = 0f;

    public Vector2Int GridPosition;

    public List<StatusEffect> StatusEffects = new();

    // Index-aligned to Fighter._abilities. Ability's other fields are static config already
    // identical on both peers (loaded from the same fighters.json) — CurrentCooldown is the
    // only per-instance mutable field on Ability, so it's the only thing pulled out here rather
    // than nesting the whole Ability object.
    public List<int> AbilityCooldowns = new();
}
