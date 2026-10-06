using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    [Serializable]
    public struct WeightedResource
    {
        public ResourceDefinition resource;
        [Tooltip("Relative weight. Weights are normalized to 100 %.")]
        [Min(0f)] public float weight;
    }

    /// <summary>
    /// Shared fields of a Mine depth band (<see cref="LayerDefinition"/>) and a Forest area
    /// (<see cref="ForestZoneDefinition"/>). All numbers are BALANCE STARTING POINTS (GDD 6/7).
    /// </summary>
    public abstract class SiteBandDefinition : ContentDefinition
    {
        [Tooltip("1-based depth (Mine) or area (Forest). Bands of a site must be numbered 1..n.")]
        [Min(1)] [SerializeField] private int index = 1;
        [Tooltip("Shown before the band is revealed as its expected tier, e.g. 'Better copper access'.")]
        [SerializeField] private string discovery = "";
        [Tooltip("Primary pool. Each primary unit draws exactly one of these.")]
        [SerializeField] private List<WeightedResource> primaryWeights = new List<WeightedResource>();
        [Tooltip("Independent bonus roll per primary unit (gem for Mine, herb for Forest). Not part of the primary percentages.")]
        [SerializeField] private ResourceDefinition rareResource;
        [Range(0f, 1f)] [SerializeField] private float rareChance;
        [Tooltip("Multiplies the curve's base durability for this band.")]
        [Min(0.01f)] [SerializeField] private float durabilityFactor = 1f;
        [Tooltip("One-way worker travel between staging and the work spot, in seconds.")]
        [Min(0.1f)] [SerializeField] private float travelSecondsPerLeg = 2.5f;
        [Tooltip("Relative weight jitter applied once when the band is generated (0.08 = ±8 %).")]
        [Range(0f, 0.5f)] [SerializeField] private float compositionVariation = 0.08f;
        [Tooltip("Resource that must keep a minimum share so recovery recipes never strand (GDD 6/7).")]
        [SerializeField] private ResourceDefinition baselineResource;
        [Range(0f, 1f)] [SerializeField] private float baselineMinShare = 0.35f;

        public int Index => index;
        public string Discovery => discovery;
        public IReadOnlyList<WeightedResource> PrimaryWeights => primaryWeights;
        public ResourceDefinition RareResource => rareResource;
        public float RareChance => rareChance;
        public float DurabilityFactor => durabilityFactor;
        public float TravelSecondsPerLeg => travelSecondsPerLeg;
        public float CompositionVariation => compositionVariation;
        public ResourceDefinition BaselineResource => baselineResource;
        public float BaselineMinShare => baselineMinShare;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            var total = 0f;
            var baselineShare = 0f;
            foreach (var w in primaryWeights)
            {
                if (w.resource == null) { errors.Add($"{Describe()}: primary weight with missing resource"); continue; }
                if (w.weight <= 0) errors.Add($"{Describe()}: weight for {w.resource.Id} must be > 0");
                total += w.weight;
                if (w.resource == baselineResource) baselineShare += w.weight;
            }
            if (total <= 0) errors.Add($"{Describe()}: primary weights must have a positive total");
            if (rareChance > 0 && rareResource == null) errors.Add($"{Describe()}: rare chance set without a rare resource");
            if (baselineResource == null) errors.Add($"{Describe()}: baseline resource missing");
            else if (total > 0 && baselineShare / total + 1e-4f < baselineMinShare)
                errors.Add($"{Describe()}: baseline {baselineResource.Id} share {baselineShare / total:P0} is below {baselineMinShare:P0}");
        }

#if UNITY_EDITOR
        public void EditorSetupBand(int newIndex, string newDiscovery, List<WeightedResource> weights, ResourceDefinition rare, float chance,
            float factor, float legSeconds, ResourceDefinition baseline, float baselineMin)
        {
            index = newIndex;
            discovery = newDiscovery;
            primaryWeights = weights;
            rareResource = rare;
            rareChance = chance;
            durabilityFactor = factor;
            travelSecondsPerLeg = legSeconds;
            baselineResource = baseline;
            baselineMinShare = baselineMin;
        }
#endif
    }
}
