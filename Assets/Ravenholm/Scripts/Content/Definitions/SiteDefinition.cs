using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    public enum SiteType
    {
        Mine,
        Forest,
    }

    /// <summary>
    /// One gathering site: its ordered bands, curve coefficients, staging pile and workers.
    /// Curve values are BALANCE STARTING POINTS from GDD 6/7.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Site Definition", fileName = "site_")]
    public sealed class SiteDefinition : ContentDefinition
    {
        [SerializeField] private SiteType siteType;
        [Tooltip("Mine: LayerDefinitions. Forest: ForestZoneDefinitions. Ordered by index 1..n; the last is the charted frontier.")]
        [SerializeField] private List<SiteBandDefinition> bands = new List<SiteBandDefinition>();
        [SerializeField] private List<WorkerDefinition> workers = new List<WorkerDefinition>();
        [Tooltip("Named recruits added one at a time by crew expansion upgrades (GDD 5).")]
        [SerializeField] private List<WorkerDefinition> recruits = new List<WorkerDefinition>();

        [Header("Staging (GDD 3)")]
        [Tooltip("World container ID of this site's staging pile.")]
        [SerializeField] private string stagingContainerId = "";
        [Min(1)] [SerializeField] private int stagingCapacity = 96;

        [Header("Yield")]
        [Tooltip("Mine: primary units per section (24). Forest: units per tree (4).")]
        [Min(1)] [SerializeField] private int unitsPerSectionOrTree = 24;
        [Tooltip("Forest only: trees per clearing (6).")]
        [Min(1)] [SerializeField] private int treesPerClearing = 6;

        [Header("Durability curve")]
        [Tooltip("Mine section or Forest tree durability at band 1 (240 / 40).")]
        [Min(1)] [SerializeField] private float baseDurability = 240f;
        [Tooltip("Growth per band (1.22 Mine / 1.18 Forest).")]
        [Min(1)] [SerializeField] private float growthPerBand = 1.22f;
        [Tooltip("Rounding step after growth (10 Mine / 5 Forest).")]
        [Min(0)] [SerializeField] private float roundTo = 10f;
        [Tooltip("Continuation multiplier 1 + linear·k + quadratic·k² (0.18 / 0.04 Mine, 0.15 / 0.03 Forest).")]
        [Min(0)] [SerializeField] private float continuationLinear = 0.18f;
        [Min(0)] [SerializeField] private float continuationQuadratic = 0.04f;
        [Tooltip("At the terminal band only, the continuation index used for durability is capped here (GDD 6).")]
        [Min(0)] [SerializeField] private int terminalContinuationCap = 6;
        [Tooltip("Mine: shaft levels per layer. Each level has its own tunnel, so this many miners can work one layer side by side.")]
        [Min(1)] [SerializeField] private int levelsPerBand = 3;

        [Header("Timing")]
        [Tooltip("Forest: seconds to walk between trees in a clearing.")]
        [Min(0.1f)] [SerializeField] private float hopSeconds = 0.8f;
        [Tooltip("Seconds a worker spends unloading at staging.")]
        [Min(0.05f)] [SerializeField] private float depositSeconds = 0.6f;
        [Tooltip("Mine: what a scaffold is built from (any one of these, from the town's stock), and how many each.")]
        [SerializeField] private List<ResourceDefinition> scaffoldMaterials = new List<ResourceDefinition>();
        [Min(0)] [SerializeField] private int scaffoldCost = 1;
        [Tooltip("Forest: seconds before a stump left standing grows back into its tree (0 = never). Cleared stumps never do.")]
        [Min(0)] [SerializeField] private float treeRegrowSeconds = 300f;

        public override string IdKind => "site";
        public SiteType SiteType => siteType;
        public IReadOnlyList<SiteBandDefinition> Bands => bands;
        public IReadOnlyList<WorkerDefinition> Workers => workers;
        public IReadOnlyList<WorkerDefinition> Recruits => recruits;
        public string StagingContainerId => stagingContainerId;
        public int StagingCapacity => stagingCapacity;
        public int UnitsPerSectionOrTree => unitsPerSectionOrTree;
        public int TreesPerClearing => treesPerClearing;
        public float BaseDurability => baseDurability;
        public float GrowthPerBand => growthPerBand;
        public float RoundTo => roundTo;
        public float ContinuationLinear => continuationLinear;
        public float ContinuationQuadratic => continuationQuadratic;
        public int TerminalContinuationCap => terminalContinuationCap;
        public int LevelsPerBand => levelsPerBand;
        public float HopSeconds => hopSeconds;
        public float DepositSeconds => depositSeconds;
        public IReadOnlyList<ResourceDefinition> ScaffoldMaterials => scaffoldMaterials;
        public int ScaffoldCost => scaffoldCost;
        public float TreeRegrowSeconds => treeRegrowSeconds;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (bands.Count == 0) errors.Add($"{Describe()}: needs at least one band");
            for (var i = 0; i < bands.Count; i++)
            {
                var b = bands[i];
                if (b == null) { errors.Add($"{Describe()}: band #{i + 1} is missing"); continue; }
                if (b.Index != i + 1) errors.Add($"{Describe()}: band '{b.Id}' has index {b.Index}, expected {i + 1}");
                var expected = siteType == SiteType.Mine ? typeof(LayerDefinition) : typeof(ForestZoneDefinition);
                if (b.GetType() != expected) errors.Add($"{Describe()}: band '{b.Id}' must be a {expected.Name}");
            }
            foreach (var w in workers) if (w == null) errors.Add($"{Describe()}: missing worker reference");
            if (string.IsNullOrEmpty(stagingContainerId)) errors.Add($"{Describe()}: staging container ID is empty");
        }

#if UNITY_EDITOR
        public void EditorSetRecruits(List<WorkerDefinition> r) => recruits = r;
        public void EditorSetWorkers(List<WorkerDefinition> w) => workers = w;
        public void EditorSetBands(List<SiteBandDefinition> b) => bands = b;

        public void EditorSetupSite(SiteType type, List<SiteBandDefinition> newBands, List<WorkerDefinition> newWorkers, string staging,
            int units, int trees, float baseD, float growth, float round, float kl, float kq, float hop)
        {
            siteType = type;
            bands = newBands;
            workers = newWorkers;
            stagingContainerId = staging;
            unitsPerSectionOrTree = units;
            treesPerClearing = trees;
            baseDurability = baseD;
            growthPerBand = growth;
            roundTo = round;
            continuationLinear = kl;
            continuationQuadratic = kq;
            hopSeconds = hop;
        }
#endif
    }
}
