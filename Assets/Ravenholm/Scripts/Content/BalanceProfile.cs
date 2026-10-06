using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>
    /// Gameplay tunables (GDD 22). Every value is a BALANCE STARTING POINT to revise through
    /// playtesting. Later phases add resolver coefficients, curves and caps here.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Config/Balance Profile", fileName = "BalanceProfile")]
    public sealed class BalanceProfile : ScriptableObject
    {
        [Header("Movement (GDD 3)")]
        [Tooltip("World units per second. GDD default 3.5.")]
        [Min(0.1f)] public float walkSpeed = 3.5f;
        [Tooltip("Radius of the player's collision footprint at the feet, in world units.")]
        [Min(0.05f)] public float playerFootprintRadius = 0.22f;

        [Header("Carrying (GDD 3)")]
        [Tooltip("Carry capacity in units. Every raw material or finished item is one unit. GDD default 12.")]
        [Min(1)] public int carryCapacity = 12;
        [Tooltip("Pickup reach beyond a pile's own radius, in world units. GDD default 0.8.")]
        [Min(0f)] public float pickupRadius = 0.8f;
        [Tooltip("Seconds per transferred unit (pickup or delivery). GDD default 0.08.")]
        [Min(0.01f)] public float transferInterval = 0.08f;
        [Tooltip("How much closer (units) an output pad is treated than a raw pile when breaking near-ties.")]
        [Min(0f)] public float outputPadPriority = 0.05f;
    }
}
