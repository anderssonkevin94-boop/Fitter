using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>How a trait changes the risk a contractor accepts when applying (GDD 10).</summary>
    public enum WillingnessRule
    {
        /// <summary>Neutral: applies at Risky or better.</summary>
        Neutral,
        /// <summary>Cautious: applies only at Good or better.</summary>
        Cautious,
        /// <summary>Bold: tolerates one band lower (applies at Dangerous).</summary>
        Bold,
    }

    public enum TraitScope
    {
        Application,
        Resolution,
        Both,
    }

    /// <summary>Contractor trait (at most two per contractor). Effects are explicit; no hidden odds (GDD 10).</summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Trait Definition", fileName = "trait_")]
    public sealed class TraitDefinition : ContentDefinition
    {
        [SerializeField] private WillingnessRule willingness;
        [SerializeField] private List<CounterValue> counters = new List<CounterValue>();
        [SerializeField] private TraitScope scope;
        [TextArea(1, 3)] [SerializeField] private string description = "";

        public override string IdKind => "trait";
        public WillingnessRule Willingness => willingness;
        public IReadOnlyList<CounterValue> Counters => counters;
        public TraitScope Scope => scope;
        public string Description => description;

#if UNITY_EDITOR
        public void EditorSetupTrait(WillingnessRule w, List<CounterValue> c, TraitScope sc, string d)
        {
            willingness = w; counters = c; scope = sc; description = d;
        }
#endif
    }
}
