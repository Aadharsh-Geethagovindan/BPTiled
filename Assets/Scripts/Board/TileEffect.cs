using System.Collections.Generic;

// Runtime representation of an active tile effect on the board.
// Owned and tracked by TileEffectManager.
public class TileEffect
{
    public string             Name;
    public int                Duration;       // remaining rounds
    public int                SourceTeam;     // team that placed this effect
    public TileEffectTrigger  Trigger;
    public TileEffectAffinity Affinity;
    public int                Damage;
    public int                Healing;
    public int                Shielding;
    public bool               DestroyOnTrigger;
    public List<AbilityStatusEffect> StatusEffectsToApply;

    public TileEffect(AbilityTileEffect data, int sourceTeam)
    {
        Name                 = data.Name;
        Duration             = data.Duration;
        SourceTeam           = sourceTeam;
        Trigger              = data.Trigger;
        Affinity             = data.Affinity;
        Damage               = data.Damage;
        Healing              = data.Healing;
        Shielding            = data.Shielding;
        DestroyOnTrigger     = data.DestroyOnTrigger;
        StatusEffectsToApply = data.StatusEffectsToApply ?? new List<AbilityStatusEffect>();
    }

    public bool AffectsFighter(Fighter fighter)
    {
        return Affinity switch
        {
            TileEffectAffinity.AllyOnly  => fighter.TeamId == SourceTeam,
            TileEffectAffinity.EnemyOnly => fighter.TeamId != SourceTeam,
            TileEffectAffinity.All       => true,
            _                            => false
        };
    }
}
