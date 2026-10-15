using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    public sealed class ContentValidationReport
    {
        public readonly List<string> Errors = new List<string>();
        public int DefinitionCount;
        public bool IsValid => Errors.Count == 0;

        public override string ToString() =>
            IsValid ? $"Content OK: {DefinitionCount} definitions" : $"Content INVALID ({Errors.Count} errors):\n- " + string.Join("\n- ", Errors);
    }

    /// <summary>
    /// Single registry of authored content. Saved records resolve IDs through here.
    /// Unknown IDs fail loudly; nothing is silently substituted (GDD 22).
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Content Catalogue", fileName = "ContentCatalogue")]
    public sealed class ContentCatalogue : ScriptableObject
    {
        [SerializeField] private List<ResourceDefinition> resources = new List<ResourceDefinition>();
        [SerializeField] private List<ItemDefinition> items = new List<ItemDefinition>();
        [SerializeField] private List<SiteDefinition> sites = new List<SiteDefinition>();
        [Tooltip("Mine layers and Forest zones.")]
        [SerializeField] private List<SiteBandDefinition> bands = new List<SiteBandDefinition>();
        [SerializeField] private List<WorkerDefinition> workers = new List<WorkerDefinition>();
        [SerializeField] private List<WorkerEquipmentDefinition> equipment = new List<WorkerEquipmentDefinition>();
        [SerializeField] private List<StationDefinition> stations = new List<StationDefinition>();
        [SerializeField] private List<ProcessDefinition> processes = new List<ProcessDefinition>();
        [SerializeField] private List<RecipeDefinition> recipes = new List<RecipeDefinition>();
        [SerializeField] private List<BlueprintDefinition> blueprints = new List<BlueprintDefinition>();
        [SerializeField] private StartingState startingState;
        [SerializeField] private List<AdventurerClassDefinition> classes = new List<AdventurerClassDefinition>();
        [SerializeField] private List<TraitDefinition> traits = new List<TraitDefinition>();
        [SerializeField] private List<ThreatDefinition> threats = new List<ThreatDefinition>();
        [SerializeField] private List<QuestDefinition> quests = new List<QuestDefinition>();
        [SerializeField] private List<ContractorDefinition> contractors = new List<ContractorDefinition>();
        [SerializeField] private ContractorNamePool namePool;
        [SerializeField] private TraderStockDefinition traderStock;
        [SerializeField] private List<UpgradeDefinition> upgrades = new List<UpgradeDefinition>();
        [SerializeField] private List<RegionDefinition> regions = new List<RegionDefinition>();
        [SerializeField] private List<TutorialStepDefinition> tutorial = new List<TutorialStepDefinition>();
        [SerializeField] private ShopDefinition shop;
        [SerializeField] private List<ShopFurnitureDefinition> shopFurniture = new List<ShopFurnitureDefinition>();
        [Tooltip("The journey's camps, in order (2026-10-15).")]
        [SerializeField] private List<CampDefinition> camps = new List<CampDefinition>();
        [Tooltip("Applied balance revisions (Phase 9). Each revision is one documented, versioned set of tuning changes.")]
        [SerializeField] private int balanceRevision;

        private Dictionary<string, ContentDefinition> _byId;

        public IReadOnlyList<ResourceDefinition> Resources => resources;
        public IReadOnlyList<ItemDefinition> Items => items;
        public IReadOnlyList<SiteDefinition> Sites => sites;
        public IReadOnlyList<SiteBandDefinition> Bands => bands;
        public IReadOnlyList<WorkerDefinition> Workers => workers;
        public IReadOnlyList<WorkerEquipmentDefinition> Equipment => equipment;
        public IReadOnlyList<StationDefinition> Stations => stations;
        public IReadOnlyList<ProcessDefinition> Processes => processes;
        public IReadOnlyList<RecipeDefinition> Recipes => recipes;
        public IReadOnlyList<BlueprintDefinition> Blueprints => blueprints;
        public StartingState StartingState => startingState;
        public ShopDefinition Shop => shop;
        public IReadOnlyList<ShopFurnitureDefinition> ShopFurniture => shopFurniture;
        public IReadOnlyList<CampDefinition> Camps => camps;
        /// <summary>The camp the company is at (camp 1 until moving on exists).</summary>
        public CampDefinition CurrentCamp => camps.Count > 0 ? camps[0] : null;
        public IReadOnlyList<AdventurerClassDefinition> Classes => classes;
        public IReadOnlyList<TraitDefinition> Traits => traits;
        public IReadOnlyList<ThreatDefinition> Threats => threats;
        public IReadOnlyList<QuestDefinition> Quests => quests;
        public IReadOnlyList<ContractorDefinition> Contractors => contractors;
        public ContractorNamePool NamePool => namePool;
        public TraderStockDefinition TraderStock => traderStock;
        public IReadOnlyList<UpgradeDefinition> Upgrades => upgrades;
        public IReadOnlyList<RegionDefinition> Regions => regions;
        public IReadOnlyList<TutorialStepDefinition> Tutorial => tutorial;
        public int BalanceRevision => balanceRevision;

        public IEnumerable<ContentDefinition> All()
        {
            foreach (var r in resources) yield return r;
            foreach (var i in items) yield return i;
            foreach (var x in sites) yield return x;
            foreach (var x in bands) yield return x;
            foreach (var x in workers) yield return x;
            foreach (var x in equipment) yield return x;
            foreach (var x in stations) yield return x;
            foreach (var x in processes) yield return x;
            foreach (var x in recipes) yield return x;
            foreach (var x in blueprints) yield return x;
            foreach (var x in classes) yield return x;
            foreach (var x in traits) yield return x;
            foreach (var x in threats) yield return x;
            foreach (var x in quests) yield return x;
            foreach (var x in contractors) yield return x;
            if (traderStock != null) yield return traderStock;
            foreach (var x in upgrades) yield return x;
            foreach (var x in regions) yield return x;
            foreach (var x in tutorial) yield return x;
            if (shop != null) yield return shop;
            foreach (var x in shopFurniture) yield return x;
            foreach (var x in camps) yield return x;
        }

        public bool TryGet<T>(string id, out T definition) where T : ContentDefinition
        {
            EnsureLookup();
            if (id != null && _byId.TryGetValue(id, out var found) && found is T typed)
            {
                definition = typed;
                return true;
            }
            definition = null;
            return false;
        }

        public T Get<T>(string id) where T : ContentDefinition
        {
            if (TryGet<T>(id, out var d)) return d;
            throw new KeyNotFoundException($"No {typeof(T).Name} with ID '{id}' in the content catalogue.");
        }

        public ContentValidationReport Validate()
        {
            var report = new ContentValidationReport();
            var seen = new Dictionary<string, ContentDefinition>();
            var index = 0;
            foreach (var def in All())
            {
                index++;
                if (def == null) { report.Errors.Add($"Catalogue entry #{index} is empty (missing reference)"); continue; }
                report.DefinitionCount++;
                def.Validate(report.Errors);
                if (string.IsNullOrEmpty(def.Id)) continue;
                if (seen.TryGetValue(def.Id, out var first))
                {
                    if (ReferenceEquals(first, def)) report.Errors.Add($"{def.Describe()}: listed twice in the catalogue");
                    else report.Errors.Add($"Duplicate ID '{def.Id}' used by '{first.name}' and '{def.name}'");
                }
                else seen.Add(def.Id, def);
            }

            // Every site band and worker must be listed in the catalogue and belong to exactly one site.
            var workerSites = new Dictionary<WorkerDefinition, SiteDefinition>();
            foreach (var site in sites)
            {
                if (site == null) continue;
                foreach (var b in site.Bands)
                    if (b != null && !bands.Contains(b)) report.Errors.Add($"{site.Describe()}: band '{b.Id}' is not in the catalogue");
                foreach (var w in site.Workers)
                {
                    if (w == null) continue;
                    if (!workers.Contains(w)) report.Errors.Add($"{site.Describe()}: worker '{w.Id}' is not in the catalogue");
                    if (workerSites.TryGetValue(w, out var other)) report.Errors.Add($"Worker '{w.Id}' is assigned to both '{other.Id}' and '{site.Id}'");
                    else workerSites[w] = site;
                }
            }

            // Every item with a recipe must reach a blueprint; blueprints should unlock something.
            foreach (var bp in blueprints)
                if (bp != null && !recipes.Exists(r => r != null && r.RequiredBlueprint == bp))
                    report.Errors.Add($"{bp.Describe()}: unlocks no recipe");
            foreach (var r in recipes)
            {
                if (r == null) continue;
                if (r.RequiredBlueprint != null && !blueprints.Contains(r.RequiredBlueprint)) report.Errors.Add($"{r.Describe()}: blueprint not in the catalogue");
                if (r.Station != null && !stations.Contains(r.Station)) report.Errors.Add($"{r.Describe()}: station not in the catalogue");
            }
            if (recipes.Count > 0 && startingState == null) report.Errors.Add("Content catalogue has recipes but no StartingState");

            if (quests.Count > 0)
            {
                // GDD 15/22: a guaranteed recovery contract and a starter weapon for every class.
                if (!quests.Exists(q => q != null && q.RecoveryContract)) report.Errors.Add("No recovery contract quest (GDD 15 requires one)");
                foreach (var c in classes)
                {
                    if (c == null) continue;
                    var ok = false;
                    foreach (var r in recipes)
                        if (r != null && r.OutputItem != null && r.RequiredBlueprint != null && r.RequiredBlueprint.OwnedAtStart &&
                            r.OutputItem.Category == ItemCategory.Weapon && ContainsTag(c.WeaponTags, r.OutputItem.WeaponTag)) ok = true;
                    if (!ok) report.Errors.Add($"{c.Describe()}: no starter weapon blueprint");
                }
                if (!contractors.Exists(x => x != null && x.Sponsored)) report.Errors.Add("No sponsored contractor for the recovery contract");
                if (namePool == null) report.Errors.Add("No contractor name pool");

                // Quest prerequisites must be acyclic (GDD 14/22).
                var state = new Dictionary<QuestDefinition, int>();
                bool Cyclic(QuestDefinition q)
                {
                    if (q == null) return false;
                    if (state.TryGetValue(q, out var st)) return st == 1;
                    state[q] = 1;
                    foreach (var p in q.Prerequisites) if (Cyclic(p)) return true;
                    state[q] = 2;
                    return false;
                }
                foreach (var q in quests) if (Cyclic(q)) { report.Errors.Add($"{q.Describe()}: prerequisite cycle"); break; }
                foreach (var q in quests)
                    if (q != null && q.FirstSuccessBlueprint != null && !blueprints.Contains(q.FirstSuccessBlueprint))
                        report.Errors.Add($"{q.Describe()}: reward blueprint not in the catalogue");
                foreach (var q in quests)
                    if (q != null && q.Region != null && !regions.Contains(q.Region)) report.Errors.Add($"{q.Describe()}: region not in the catalogue");

                // Phase 7: the whole progression must be reachable through ordinary play (no rare-drop walls).
                if (report.Errors.Count == 0) report.Errors.AddRange(ProgressionGraph.Problems(this));
            }
            return report;
        }

        private static bool ContainsTag(IReadOnlyList<WeaponTag> tags, WeaponTag t)
        {
            foreach (var x in tags) if (x == t) return true;
            return false;
        }

        private void EnsureLookup()
        {
            if (_byId != null) return;
            _byId = new Dictionary<string, ContentDefinition>();
            foreach (var def in All())
                if (def != null && !string.IsNullOrEmpty(def.Id) && !_byId.ContainsKey(def.Id))
                    _byId.Add(def.Id, def);
        }

        private void OnValidate() { _byId = null; }

#if UNITY_EDITOR
        public void EditorSet(List<ResourceDefinition> newResources, List<ItemDefinition> newItems)
        {
            resources = newResources;
            items = newItems;
            _byId = null;
        }

        public void EditorSetProduction(List<StationDefinition> newStations, List<ProcessDefinition> newProcesses, List<RecipeDefinition> newRecipes,
            List<BlueprintDefinition> newBlueprints, StartingState start)
        {
            stations = newStations;
            processes = newProcesses;
            recipes = newRecipes;
            blueprints = newBlueprints;
            startingState = start;
            _byId = null;
        }

        public void EditorSetBalanceRevision(int r) => balanceRevision = r;

        public void EditorSetCamps(List<CampDefinition> newCamps)
        {
            camps = newCamps;
            _byId = null;
        }

        public void EditorSetShop(ShopDefinition newShop, List<ShopFurnitureDefinition> furniture)
        {
            shop = newShop;
            shopFurniture = furniture;
            _byId = null;
        }

        public void EditorSetTutorial(List<TutorialStepDefinition> steps)
        {
            tutorial = steps;
            _byId = null;
        }

        public void EditorSetRegions(List<RegionDefinition> newRegions)
        {
            regions = newRegions;
            _byId = null;
        }

        public void EditorSetQuests(List<QuestDefinition> newQuests, List<ThreatDefinition> newThreats)
        {
            quests = newQuests;
            threats = newThreats;
            _byId = null;
        }

        public void EditorSetEconomy(TraderStockDefinition trader, List<UpgradeDefinition> newUpgrades)
        {
            traderStock = trader;
            upgrades = newUpgrades;
            _byId = null;
        }

        public void EditorSetJobs(List<AdventurerClassDefinition> newClasses, List<TraitDefinition> newTraits, List<ThreatDefinition> newThreats,
            List<QuestDefinition> newQuests, List<ContractorDefinition> newContractors, ContractorNamePool pool)
        {
            classes = newClasses;
            traits = newTraits;
            threats = newThreats;
            quests = newQuests;
            contractors = newContractors;
            namePool = pool;
            _byId = null;
        }

        public void EditorSetSites(List<SiteDefinition> newSites, List<SiteBandDefinition> newBands, List<WorkerDefinition> newWorkers,
            List<WorkerEquipmentDefinition> newEquipment)
        {
            sites = newSites;
            bands = newBands;
            workers = newWorkers;
            equipment = newEquipment;
            _byId = null;
        }
#endif
    }
}
