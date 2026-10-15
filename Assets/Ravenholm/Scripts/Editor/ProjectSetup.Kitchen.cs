using System.Collections.Generic;
using System.IO;
using Ravenholm.Content;
using UnityEditor;

namespace Ravenholm.EditorTools
{
    public static partial class ProjectSetup
    {
        /// <summary>
        /// The crafting expansion, part 3 (Kevin, 2026-10-16): the Workbench becomes the Workshop (plus light leather and
        /// cloth armour); the Kitchen arrives as a kit and cooks food from what the forest gives (berries under felled trees,
        /// mushrooms among pulled stumps). Food is a cheap consumable, always eaten on the trip. Balance starting points.
        /// </summary>
        private static void EnsureKitchen(ContentCatalogue catalogue)
        {
            ItemDefinition I(string key) => catalogue.Get<ItemDefinition>("item." + key);
            ResourceDefinition R(string key) => catalogue.Get<ResourceDefinition>("resource." + key);
            List<ResourceCost> M(params (string r, int q)[] c)
            {
                var l = new List<ResourceCost>();
                foreach (var (r, qty) in c) l.Add(new ResourceCost { resource = R(r), quantity = qty });
                return l;
            }

            // The Workshop (the Workbench's new name; its id stays).
            var bench = catalogue.Get<StationDefinition>("station.workbench");
            if (bench.DisplayName != "Workshop") { bench.EditorInitialize(bench.Id, "Workshop"); EditorUtility.SetDirty(bench); }

            // The Kitchen: a station that arrives as a kit.
            var kitchenPath = ProductionDir + "station_kitchen.asset";
            var kitchenCreated = !File.Exists(kitchenPath);
            var kitchen = EnsureAsset<StationDefinition>(kitchenPath);
            if (kitchenCreated)
            {
                kitchen.EditorInitialize("station.kitchen", "Kitchen");
                kitchen.EditorSetupStation("container.kitchen_pad", 6, 1, 2);
                kitchen.EditorSetStartsAsKit(true);
                EditorUtility.SetDirty(kitchen);
            }
            var stations = new List<StationDefinition>(catalogue.Stations);
            var changed = false;
            if (!stations.Contains(kitchen)) { stations.Add(kitchen); changed = true; }

            // Item stats (content authoring, every run).
            void Food(string key, int survival, int readiness)
            {
                if (I(key).FoodSurvival == survival && I(key).FoodReadiness == readiness) return;
                I(key).EditorSetFood(survival, readiness);
                EditorUtility.SetDirty(I(key));
            }
            Food("trail_rations", 2, 1);
            Food("mushroom_stew", 3, 2);
            Food("hearty_pie", 4, 3);
            void Counter(string key, CounterTag tag, int strength)
            {
                if (I(key).CounterTag == tag && I(key).CounterStrength == strength) return;
                I(key).EditorSetCounter(tag, strength);
                EditorUtility.SetDirty(I(key));
            }
            Counter("leather_jerkin", CounterTag.Traps, 2);
            Counter("padded_gambeson", CounterTag.Exploration, 2);

            // Foraging in the forest.
            var forest = catalogue.Get<SiteDefinition>("site.forest");
            if (forest.FellForage != R("berries") || forest.StumpForage != R("mushroom"))
            {
                forest.EditorSetForage(R("berries"), 0.12f, R("mushroom"), 0.35f);
                EditorUtility.SetDirty(forest);
            }

            // Recipes, owned at the start.
            var recipes = new List<RecipeDefinition>(catalogue.Recipes);
            var blueprints = new List<BlueprintDefinition>(catalogue.Blueprints);
            void Recipe(string item, StationDefinition st, List<ResourceCost> cost, int level, string reveal)
            {
                var bpPath = ProductionDir + "blueprint_" + item + ".asset";
                var bpCreated = !File.Exists(bpPath);
                var bp = EnsureAsset<BlueprintDefinition>(bpPath);
                if (bpCreated) { bp.EditorInitialize("blueprint." + item, I(item).DisplayName + " blueprint"); bp.EditorSetupBlueprint(true, reveal); EditorUtility.SetDirty(bp); }
                var rPath = ProductionDir + "recipe_" + item + ".asset";
                var rCreated = !File.Exists(rPath);
                var r = EnsureAsset<RecipeDefinition>(rPath);
                if (rCreated) { r.EditorInitialize("recipe." + item, I(item).DisplayName); r.EditorSetupRecipe(I(item), cost, st, level, bp); EditorUtility.SetDirty(r); }
                if (!recipes.Contains(r)) { recipes.Add(r); changed = true; }
                if (!blueprints.Contains(bp)) { blueprints.Add(bp); changed = true; }
            }
            Recipe("leather_jerkin", bench, M(("fur", 2), ("rope", 1)), 1, "Light leather armour: handy on trapped ground.");
            Recipe("padded_gambeson", bench, M(("rope", 2), ("cedar_lumber", 1)), 1, "Quilted cloth armour: light enough for long, wild routes.");
            Recipe("trail_rations", kitchen, M(("berries", 2)), 1, "Dried berries for the road: a little stronger, a little safer.");
            Recipe("mushroom_stew", kitchen, M(("mushroom", 2), ("berries", 1)), 1, "A hot stew before the road: well fed.");
            Recipe("hearty_pie", kitchen, M(("berries", 2), ("mushroom", 2), ("herb", 1)), 2, "A proper meal for the hardest jobs.");
            if (changed)
            {
                catalogue.EditorSetProduction(stations, new List<ProcessDefinition>(catalogue.Processes), recipes, blueprints, catalogue.StartingState);
                EditorUtility.SetDirty(catalogue);
            }

            // Kitchen 2: the Hearty Pie.
            var upPath = EconomyDir + "upgrade_kitchen_2.asset";
            var upCreated = !File.Exists(upPath);
            var up = EnsureAsset<UpgradeDefinition>(upPath);
            if (upCreated)
            {
                up.EditorInitialize("upgrade.kitchen_2", "Kitchen 2");
                up.EditorSetupUpgrade(UpgradeKind.StationLevel, kitchen, null, 2, 50, M(("iron_ingot", 4), ("maple_lumber", 4)), "Enables the Hearty Pie",
                    null, null, 0, null, 0);
                EditorUtility.SetDirty(up);
            }
            var ups = new List<UpgradeDefinition>(catalogue.Upgrades);
            if (!ups.Contains(up)) { ups.Add(up); catalogue.EditorSetEconomy(catalogue.TraderStock, ups); EditorUtility.SetDirty(catalogue); }
        }
    }
}
