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
        [Tooltip("World units per second. GDD default 3.5; 2.6 since the dwarf (2026-10-13, Kevin: a little slower).")]
        [Min(0.1f)] public float walkSpeed = 2.6f;
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
    
        [Header("Company start (GDD 15)")]
        [Min(0)] public int startingGold = 60;
        [Range(0, 100)] public int startingReputation = 20;

        [Header("Readiness A (GDD 12)")]
        public float mightWeight = 0.45f;
        public float survivalWeight = 0.30f;
        public float insightWeight = 0.25f;
        public float weaponPowerWeight = 0.80f;
        public float armorProtectionWeight = 0.65f;
        [Tooltip("Net context C is clamped to ±this.")]
        public float contextCap = 8f;

        [Header("Survival V (GDD 12)")]
        public float survivalProtectionWeight = 0.80f;
        public float survivalDifficultyWeight = 0.55f;
        [Tooltip("Survival context is clamped to ±this.")]
        public float survivalContextCap = 4f;

        [Header("Risk words from the mean execution margin (GDD 12)")]
        public float excellentAt = 10f;
        public float goodAt = 3f;
        public float riskyAt = -3f;
        public float dangerousAt = -9f;

        [Header("Fees (GDD 10)")]
        [Tooltip("Fee × (1 + this × (level − 1)).")]
        public float feePerLevel = 0.12f;
        [Tooltip("Fee × (1 + difficulty / this).")]
        public float feeDifficultyDivisor = 40f;
        [Tooltip("reputationPrice = clamp(base − reputation / divisor, min, max).")]
        public float repPriceBase = 1.15f;
        public float repPriceDivisor = 250f;
        public float repPriceMin = 0.80f;
        public float repPriceMax = 1.15f;

        [Header("Job Board (GDD 11)")]
        [Min(1)] public int activeExpeditionSlots = 2;
        [Min(1)] public int applicantsBase = 2;
        [Tooltip("Reputation at which a third and a fourth applicant come.")]
        public int thirdApplicantAt = 30;
        public int fourthApplicantAt = 60;
        [Min(0)] public float applicationMinSeconds = 10f;
        [Min(0)] public float applicationMaxSeconds = 30f;
        [Tooltip("Approximate size of the persistent contractor pool.")]
        [Min(4)] public int contractorPoolTarget = 12;
    }
}
