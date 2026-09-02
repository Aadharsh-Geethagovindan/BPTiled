using UnityEngine;

// Adds a team-colored ambient particle aura around a fighter, instantiated from a prefab
// (Assets/Prefabs/Particles/ActiveFighterAura.prefab) so the particle system itself — shape,
// size, emission, lifetime, sorting — is tunable natively in the Unity Editor. Nothing about the
// effect is hardcoded here; this only applies the per-fighter team tint after instantiating.
public class FighterTeamAura : MonoBehaviour
{
    public void Initialize(GameObject auraPrefab, Color color)
    {
        if (auraPrefab == null) return;

        var auraObj   = Instantiate(auraPrefab, transform);
        var particles = auraObj.GetComponentInChildren<ParticleSystem>();
        if (particles == null) return;

        var main = particles.main;
        main.startColor = color;
    }
}
