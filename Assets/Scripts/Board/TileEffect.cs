using System.Collections.Generic;

// Runtime representation of an active tile effect on the board.
// Owned and tracked by TileEffectManager.
[System.Serializable]
public class TileEffect
{
    public string             Name;
    public int                Duration;          // remaining rounds
    public int                SourceTeam;        // team that placed this effect
    public string             SourceFighterName; // who placed it — grants them charge when it deals damage/heal/shield
    public TileEffectTrigger  Trigger;
    public TileEffectAffinity Affinity;
    public int                Damage;
    public int                Healing;
    public int                Shielding;
    public bool               DestroyOnTrigger;
    public List<AbilityStatusEffect> StatusEffectsToApply;
    public DynamicValue       DynamicValue;      // null if this effect's values are all fixed
    public string             ExcludedSpecies;   // e.g. "Riftbeast" — unaffected regardless of affinity
    public float              RemoveRandomBuffChance; // 0-1; on trigger, chance to strip one random buff

    // Parameterless constructor for JsonUtility deserialization during network sync.
    public TileEffect() { }

    public TileEffect(AbilityTileEffect data, Fighter source)
    {
        Name                 = data.Name;
        Duration             = data.Duration;
        SourceTeam           = source.TeamId;
        SourceFighterName    = source.FighterName;
        Trigger              = data.Trigger;
        Affinity             = data.Affinity;
        Damage               = data.Damage;
        Healing              = data.Healing;
        Shielding            = data.Shielding;
        DestroyOnTrigger     = data.DestroyOnTrigger;
        StatusEffectsToApply = data.StatusEffectsToApply ?? new List<AbilityStatusEffect>();
        DynamicValue         = data.DynamicValue;
        ExcludedSpecies      = data.ExcludedSpecies;
        RemoveRandomBuffChance = data.RemoveRandomBuffChance;
    }

    public bool AffectsFighter(Fighter fighter)
    {
        bool affinityMatch = Affinity switch
        {
            TileEffectAffinity.AllyOnly  => fighter.TeamId == SourceTeam,
            TileEffectAffinity.EnemyOnly => fighter.TeamId != SourceTeam,
            TileEffectAffinity.All       => true,
            _                            => false
        };
        if (!affinityMatch) return false;

        if (!string.IsNullOrEmpty(ExcludedSpecies) && fighter.Species == ExcludedSpecies) return false;

        return true;
    }
}

// One (position, effect) pair — network sync flattens TileEffectManager's
// Dictionary<Vector2Int, List<TileEffect>> into a list of these, since JsonUtility can't
// serialize a Dictionary directly. See TileEffectManager.CaptureSnapshot/ApplyNetworkSnapshot.
[System.Serializable]
public class TileEffectSnapshotEntry
{
    public int X;
    public int Y;
    public TileEffect Effect;
}

// Wrapper so the flattened list can be the root object passed to JsonUtility.ToJson/FromJson
// (which requires a class, not a bare List<T>, at the top level).
[System.Serializable]
public class TileEffectsWireFormat
{
    public List<TileEffectSnapshotEntry> Entries = new();
}
