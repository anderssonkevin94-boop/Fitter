using System.Collections.Generic;
using System.IO;
using Ravenholm.Content;
using UnityEditor;

namespace Ravenholm.EditorTools
{
    public static partial class ProjectSetup
    {
        /// <summary>
        /// The crafting expansion, part 1 (Kevin, 2026-10-16): the loadout's new slots. Helmets guard against one threat (a
        /// counter that is never used up); shields block a share of killing blows; mauls, bows and staves are two-handed
        /// (the Moonsilver Focus stays one-handed). Stats are set every run (content authoring); recipes are created once,
        /// owned at the start and gated by the station's level. Balance starting points.
        /// </summary>
        private static void EnsureArmoury(ContentCatalogue catalogue)
        {
            ItemDefinition I(string key) => catalogue.Get<ItemDefinition>("item." + key);
            List<ResourceCost> M(params (string r, int q)[] c)
            {
                var l = new List<ResourceCost>();
                foreach (var (r, qty) in c) l.Add(new ResourceCost { resource = catalogue.Get<ResourceDefinition>("resource." + r), quantity = qty });
                return l;
            }

            foreach (var key in new[] { "steel_maul", "cedar_bow", "maple_longbow", "ironwood_greatbow", "cedar_staff", "copper_ward_staff" })
                if (!I(key).TwoHanded) { I(key).EditorSetHands(true); EditorUtility.SetDirty(I(key)); }

            void Guard(string key, CounterTag tag, int strength)
            {
                var i = I(key);
                if (i.CounterTag == tag && i.CounterStrength == strength) return;
                i.EditorSetCounter(tag, strength);
                EditorUtility.SetDirty(i);
            }
            Guard("iron_helm", CounterTag.HeavyBlows, 3);
            Guard("leather_cap", CounterTag.Traps, 3);
            Guard("steel_helm", CounterTag.HeavyBlows, 5);
            Guard("moonsilver_circlet", CounterTag.Arcane, 5);
            void Block(string key, int percent)
            {
                if (I(key).Block == percent) return;
                I(key).EditorSetBlock(percent);
                EditorUtility.SetDirty(I(key));
            }
            Block("wooden_shield", 15);
            Block("iron_shield", 25);
            Block("steel_kite_shield", 35);

            var forge = catalogue.Get<StationDefinition>("station.forge");
            var bench = catalogue.Get<StationDefinition>("station.workbench");
            var recipes = new List<RecipeDefinition>(catalogue.Recipes);
            var blueprints = new List<BlueprintDefinition>(catalogue.Blueprints);
            var changed = false;
            void Recipe(string item, StationDefinition st, List<ResourceCost> cost, int level, string reveal)
            {
                BlueprintDefinition bp;
                var bpPath = ProductionDir + "blueprint_" + item + ".asset";
                var bpCreated = !File.Exists(bpPath);
                bp = EnsureAsset<BlueprintDefinition>(bpPath);
                if (bpCreated) { bp.EditorInitialize("blueprint." + item, I(item).DisplayName + " blueprint"); bp.EditorSetupBlueprint(true, reveal); EditorUtility.SetDirty(bp); }
                var rPath = ProductionDir + "recipe_" + item + ".asset";
                var rCreated = !File.Exists(rPath);
                var r = EnsureAsset<RecipeDefinition>(rPath);
                if (rCreated) { r.EditorInitialize("recipe." + item, I(item).DisplayName); r.EditorSetupRecipe(I(item), cost, st, level, bp); EditorUtility.SetDirty(r); }
                if (!recipes.Contains(r)) { recipes.Add(r); changed = true; }
                if (!blueprints.Contains(bp)) { blueprints.Add(bp); changed = true; }
            }
            Recipe("iron_helm", forge, M(("iron_ingot", 2)), 1, "Guards a warrior's head against heavy blows.");
            Recipe("leather_cap", bench, M(("fur", 2), ("rope", 1)), 1, "A padded cap that spares the head on trapped ground.");
            Recipe("steel_helm", forge, M(("steel_ingot", 3)), 2, "A closed steel helm: heavy blows glance off.");
            Recipe("moonsilver_circlet", forge, M(("moonsilver_ingot", 2), ("arcane_dust", 1)), 3, "A circlet that turns arcane harm.");
            Recipe("wooden_shield", bench, M(("cedar_lumber", 3)), 1, "A plank shield: now and then it stops the worst.");
            Recipe("iron_shield", forge, M(("iron_ingot", 2), ("cedar_lumber", 2)), 1, "Iron bands on oak: it blocks more often.");
            Recipe("steel_kite_shield", forge, M(("steel_ingot", 3), ("maple_lumber", 1)), 2, "A tall steel shield that saves lives.");
            // Timed crafting (Kevin, 2026-10-16: long, Shop Titans style). Balance starting points by tier: the six starters
            // 5 minutes (the starter tonic 2), other level-1 pieces 10, level-2 pieces 30 (level-2 potions 10), level 3 two hours.
            var starters = new HashSet<string> { "item.iron_sword", "item.cedar_bow", "item.iron_dagger", "item.cedar_staff", "item.riveted_coat" };
            foreach (var r in recipes)
            {
                if (r == null || r.OutputItem == null) continue;
                var potion = r.OutputItem.Category == ItemCategory.Potion;
                var seconds = r.MinFacilityLevel >= 3 ? 7200f
                    : r.MinFacilityLevel == 2 ? (potion ? 600f : 1800f)
                    : potion ? 120f
                    : starters.Contains(r.OutputItem.Id) ? 300f : 600f;
                if (r.CraftSeconds == seconds) continue;
                r.EditorSetCraftSeconds(seconds);
                EditorUtility.SetDirty(r);
            }
            if (changed)
            {
                catalogue.EditorSetProduction(new List<StationDefinition>(catalogue.Stations), new List<ProcessDefinition>(catalogue.Processes), recipes, blueprints,
                    catalogue.StartingState);
                EditorUtility.SetDirty(catalogue);
            }
        }
    }
}
