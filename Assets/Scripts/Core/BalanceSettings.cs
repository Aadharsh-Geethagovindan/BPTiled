using UnityEngine;

// Create via: right-click in Project → Create → Breakpoint → Balance Settings
// Assign to BattleController's Balance Settings slot.
// Future: populate these fields from a pre-match customization panel.
[CreateAssetMenu(fileName = "BalanceSettings", menuName = "Breakpoint/Balance Settings")]
public class BalanceSettings : ScriptableObject
{
    [Header("Global Stat Modifiers")]
    [Tooltip("Multiplied against base HP from JSON.  1 = unchanged, 1.5 = +50%")]
    public float hpMultiplier        = 1f;

    [Tooltip("Added to base speed from JSON.  0 = unchanged, 1 = everyone moves one extra tile")]
    public float speedAdd            = 0f;

    [Tooltip("Multiplied against base sig charge requirement.  1 = unchanged, 0.5 = charges twice as fast")]
    public float sigChargeMultiplier = 1f;
}
