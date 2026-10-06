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

        private Dictionary<string, ContentDefinition> _byId;

        public IReadOnlyList<ResourceDefinition> Resources => resources;
        public IReadOnlyList<ItemDefinition> Items => items;
        public IReadOnlyList<SiteDefinition> Sites => sites;
        public IReadOnlyList<SiteBandDefinition> Bands => bands;
        public IReadOnlyList<WorkerDefinition> Workers => workers;
        public IReadOnlyList<WorkerEquipmentDefinition> Equipment => equipment;

        public IEnumerable<ContentDefinition> All()
        {
            foreach (var r in resources) yield return r;
            foreach (var i in items) yield return i;
            foreach (var x in sites) yield return x;
            foreach (var x in bands) yield return x;
            foreach (var x in workers) yield return x;
            foreach (var x in equipment) yield return x;
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
            return report;
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
