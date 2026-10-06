using System.Collections.Generic;

namespace Ravenholm.Content
{
    /// <summary>
    /// Static reachability of the whole progression from a fresh company (GDD 22/27 Phase 7): which
    /// materials, blueprints, facility levels, quests and upgrades ordinary play can reach. Only
    /// renewable sources count: site yields (including per-unit bonus rolls, which recur every section),
    /// processing, recurring Trader stock (including the side-blueprint slot, which rolls again every
    /// visit until each one is owned), deterministic quest rewards and first-success blueprints.
    /// Quest rare rolls never count, so nothing required can hide behind a rare drop.
    /// </summary>
    public sealed class ProgressionGraph
    {
        public readonly HashSet<ResourceDefinition> Materials = new HashSet<ResourceDefinition>();
        public readonly HashSet<BlueprintDefinition> Blueprints = new HashSet<BlueprintDefinition>();
        public readonly HashSet<QuestDefinition> Succeedable = new HashSet<QuestDefinition>();
        public readonly HashSet<UpgradeDefinition> Upgrades = new HashSet<UpgradeDefinition>();
        public readonly HashSet<ItemDefinition> Craftable = new HashSet<ItemDefinition>();
        public readonly Dictionary<StationDefinition, int> StationLevels = new Dictionary<StationDefinition, int>();

        public static ProgressionGraph Build(ContentCatalogue c)
        {
            var g = new ProgressionGraph();
            if (c.StartingState != null) foreach (var m in c.StartingState.SharedStock) if (m.resource != null) g.Materials.Add(m.resource);
            foreach (var b in c.Blueprints) if (b != null && b.OwnedAtStart) g.Blueprints.Add(b);
            foreach (var s in c.Stations) if (s != null) g.StationLevels[s] = s.StartingLevel;
            // Every band of every site can be reached by choosing to go deeper; each yields its primaries and bonus roll.
            foreach (var site in c.Sites)
            {
                if (site == null) continue;
                foreach (var band in site.Bands)
                {
                    if (band == null) continue;
                    foreach (var w in band.PrimaryWeights) if (w.resource != null && w.weight > 0) g.Materials.Add(w.resource);
                    if (band.RareResource != null && band.RareChance > 0) g.Materials.Add(band.RareResource);
                }
            }
            if (c.TraderStock != null)
                foreach (var e in c.TraderStock.Entries)
                    if (e.resource != null && e.availability != StockAvailability.AfterQuest) g.Materials.Add(e.resource);
            if (c.TraderStock != null && c.TraderStock.Entries.Exists(e => e.sideBlueprint))
                foreach (var b in c.TraderStock.SideBlueprints) if (b != null) g.Blueprints.Add(b);

            var changed = true;
            var guard = 0;
            while (changed && guard++ < 200)
            {
                changed = false;
                foreach (var p in c.Processes)
                {
                    if (p == null || p.Output == null || g.Materials.Contains(p.Output) || g.Level(p.Facility) < p.MinFacilityLevel) continue;
                    if (g.Has(p.Inputs)) changed |= g.Materials.Add(p.Output);
                }
                foreach (var r in c.Recipes)
                {
                    if (r == null || r.OutputItem == null || g.Craftable.Contains(r.OutputItem)) continue;
                    if (r.RequiredBlueprint != null && !g.Blueprints.Contains(r.RequiredBlueprint)) continue;
                    if (g.Level(r.Station) < r.MinFacilityLevel || !g.Has(r.Cost)) continue;
                    changed |= g.Craftable.Add(r.OutputItem);
                }
                foreach (var u in c.Upgrades)
                {
                    if (u == null || g.Upgrades.Contains(u)) continue;
                    if (u.RequiresUpgrade != null && !g.Upgrades.Contains(u.RequiresUpgrade)) continue;
                    if (u.RequiresQuestSuccess != null && !g.Succeedable.Contains(u.RequiresQuestSuccess)) continue;
                    if (u.RequiresOrdinarySuccesses > 0 && !g.Succeedable.Exists(q => !q.RecoveryContract)) continue;
                    if (!g.Has(u.MaterialCost)) continue;
                    g.Upgrades.Add(u);
                    if (u.Kind == UpgradeKind.StationLevel && u.Station != null && g.Level(u.Station) < u.Level) g.StationLevels[u.Station] = u.Level;
                    changed = true;
                }
                foreach (var q in c.Quests)
                {
                    if (q == null || g.Succeedable.Contains(q)) continue;
                    if (!g.Unlockable(q) || g.DispatchProblem(q) != null) continue;
                    g.Succeedable.Add(q);
                    foreach (var m in q.SuccessMaterials) if (m.resource != null) g.Materials.Add(m.resource);
                    if (q.FirstSuccessBlueprint != null) g.Blueprints.Add(q.FirstSuccessBlueprint);
                    if (c.TraderStock != null)
                        foreach (var e in c.TraderStock.Entries)
                            if (e.resource != null && e.availability == StockAvailability.AfterQuest && e.afterQuest == q) g.Materials.Add(e.resource);
                    changed = true;
                }
            }
            return g;
        }

        public int Level(StationDefinition s) => s != null && StationLevels.TryGetValue(s, out var l) ? l : 1;

        public bool Has(IReadOnlyList<ResourceCost> costs)
        {
            foreach (var x in costs) if (x.resource == null || !Materials.Contains(x.resource)) return false;
            return true;
        }

        private bool Unlockable(QuestDefinition q)
        {
            foreach (var p in q.Prerequisites) if (p == null || !Succeedable.Contains(p)) return false;
            return true;
        }

        /// <summary>Why the dispatch requirements cannot be met with reachable gear (null = they can).</summary>
        public string DispatchProblem(QuestDefinition q)
        {
            if (q.MinWeaponPower > 0 && !Craftable.Exists(i => i.Category == ItemCategory.Weapon && i.Power >= q.MinWeaponPower))
                return $"no reachable weapon with Power {q.MinWeaponPower}";
            if (q.MinArmorProtection > 0 && !Craftable.Exists(i => i.Category == ItemCategory.Armor && i.Protection >= q.MinArmorProtection))
                return $"no reachable armor with Protection {q.MinArmorProtection}";
            if (q.RequireCounterPotion)
            {
                var ok = false;
                foreach (var i in Craftable)
                {
                    if (i.Category != ItemCategory.Potion) continue;
                    var tag = i.PotionCounterTag;
                    foreach (var t in q.Threats) if (t.threat != null && tag != CounterTag.None && t.threat.CounterTag == tag) ok = true;
                }
                if (!ok) return "no reachable potion counters any of its threats";
            }
            return null;
        }

        /// <summary>Everything in the catalogue that ordinary play can never reach.</summary>
        public static List<string> Problems(ContentCatalogue c)
        {
            var g = Build(c);
            var errors = new List<string>();
            foreach (var r in c.Recipes)
                if (r != null && r.OutputItem != null && !g.Craftable.Contains(r.OutputItem))
                    errors.Add($"{r.Describe()}: never craftable from a fresh start (blueprint, facility level or materials unreachable)");
            foreach (var q in c.Quests)
                if (q != null && !g.Succeedable.Contains(q))
                    errors.Add($"{q.Describe()}: unreachable ({(g.Unlockable(q) ? g.DispatchProblem(q) : "prerequisites unreachable")})");
            foreach (var u in c.Upgrades)
                if (u != null && !g.Upgrades.Contains(u)) errors.Add($"{u.Describe()}: never purchasable from a fresh start");
            foreach (var p in c.Processes)
                if (p != null && p.Output != null && !g.Materials.Contains(p.Output)) errors.Add($"{p.Describe()}: output never produced");
            // The breadcrumb must never point at a job whose own main-chain prerequisites come later.
            foreach (var q in c.Quests)
                if (q != null && q.CampaignStep > 0)
                    foreach (var p in q.Prerequisites)
                        if (p != null && p.CampaignStep >= q.CampaignStep) errors.Add($"{q.Describe()}: campaign step {q.CampaignStep} comes before its prerequisite {p.Id}");
            foreach (var q in c.Quests)
                if (q != null && q.CampaignFinale)
                    foreach (var p in q.Prerequisites)
                        if (p != null && p.CampaignStep == 0) errors.Add($"{q.Describe()}: finale prerequisite {p.Id} is not on the campaign breadcrumb");
            var finales = c.Quests.FindAll(q => q != null && q.CampaignFinale);
            if (c.Quests.Count > 0 && finales.Count != 1) errors.Add($"Exactly one campaign finale quest is required (found {finales.Count})");
            return errors;
        }
    }

    internal static class ProgressionListExtensions
    {
        public static bool Exists<T>(this HashSet<T> set, System.Predicate<T> match)
        {
            foreach (var x in set) if (match(x)) return true;
            return false;
        }

        public static bool Exists<T>(this IReadOnlyList<T> list, System.Predicate<T> match)
        {
            foreach (var x in list) if (match(x)) return true;
            return false;
        }

        public static List<T> FindAll<T>(this IReadOnlyList<T> list, System.Predicate<T> match)
        {
            var r = new List<T>();
            foreach (var x in list) if (match(x)) r.Add(x);
            return r;
        }
    }
}
