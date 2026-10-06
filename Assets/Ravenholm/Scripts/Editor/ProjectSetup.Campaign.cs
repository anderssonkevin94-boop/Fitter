using System.Collections.Generic;
using System.IO;
using Ravenholm.Content;
using UnityEditor;
using UnityEngine;

namespace Ravenholm.EditorTools
{
    public static partial class ProjectSetup
    {
        public const string CampaignDir = Root + "Content/Campaign/";

        /// <summary>
        /// Phase 7 world content (GDD 6/7/9/14/17, all balance starting points): Mine depths and Forest
        /// areas 4-6, moonsilver and ironwood processing, the advanced recipe set, three map regions,
        /// Hollow March and Crownward Ruins jobs and the Beacon chain. Creates missing assets only and
        /// appends to the catalogue without disturbing Inspector edits.
        /// </summary>
        private static void EnsureCampaignContent(ContentCatalogue catalogue)
        {
            ResourceDefinition R(string key) => catalogue.Get<ResourceDefinition>("resource." + key);
            ItemDefinition I(string key) => catalogue.Get<ItemDefinition>("item." + key);
            List<ResourceCost> M(params (string r, int q)[] c)
            {
                var l = new List<ResourceCost>();
                foreach (var (r, qty) in c) l.Add(new ResourceCost { resource = R(r), quantity = qty });
                return l;
            }
            T Make<T>(string dir, string file, string id, string name, out bool created) where T : ContentDefinition
            {
                var path = dir + file + ".asset";
                created = !File.Exists(path);
                var a = EnsureAsset<T>(path);
                if (created) a.EditorInitialize(id, name);
                return a;
            }

            // ---------------------------------------------------------------- materials and bands
            var tints = new Dictionary<string, Color32>
            {
                { "moonsilver_ore", new Color32(206, 222, 246, 255) }, { "ironwood_log", new Color32(96, 70, 58, 255) },
            };
            foreach (var kv in tints)
            {
                var r = R(kv.Key);
                if (r.MapTint != new Color(0.6f, 0.6f, 0.6f, 1f)) continue;
                r.EditorSetTint(kv.Value);
                EditorUtility.SetDirty(r);
            }

            T Band<T>(string key, string name, int index, string discovery, (string res, float w)[] weights, string rare, float chance, float leg, string baseline)
                where T : SiteBandDefinition
            {
                var b = Make<T>(SiteDir, key.Replace('.', '_'), key, name, out var created);
                if (!created) return b;
                var list = new List<WeightedResource>();
                foreach (var (res, w) in weights) list.Add(new WeightedResource { resource = R(res), weight = w });
                b.EditorSetupBand(index, discovery, list, R(rare), chance, 1f, leg, R(baseline), 0.35f);
                EditorUtility.SetDirty(b);
                return b;
            }
            // GDD 6: depths 4-6 keep at least 35 % iron; GDD 7: areas 4-6 keep at least 35 % cedar, legs capped near 6 s.
            var deepLayers = new List<SiteBandDefinition>
            {
                Band<LayerDefinition>("layer.mine_4", "Silver seam", 4, "Moonsilver traces", new[] { ("iron_ore", 50f), ("copper_ore", 15f), ("coal", 20f), ("moonsilver_ore", 15f) }, "gem", 0.02f, 3.5f, "iron_ore"),
                Band<LayerDefinition>("layer.mine_5", "Deep vein", 5, "Rich moonsilver", new[] { ("iron_ore", 40f), ("copper_ore", 10f), ("coal", 25f), ("moonsilver_ore", 25f) }, "gem", 0.025f, 3.5f, "iron_ore"),
                Band<LayerDefinition>("layer.mine_6", "Heartstone", 6, "The deepest charted rock", new[] { ("iron_ore", 35f), ("copper_ore", 10f), ("coal", 20f), ("moonsilver_ore", 35f) }, "gem", 0.03f, 3.5f, "iron_ore"),
            };
            var deepZones = new List<SiteBandDefinition>
            {
                Band<ForestZoneDefinition>("zone.forest_4", "Deep grove", 4, "First ironwood", new[] { ("cedar_log", 45f), ("maple_log", 40f), ("ironwood_log", 15f) }, "herb", 0.09f, 5f, "cedar_log"),
                Band<ForestZoneDefinition>("zone.forest_5", "Old growth", 5, "Ironwood stands", new[] { ("cedar_log", 40f), ("maple_log", 30f), ("ironwood_log", 30f) }, "herb", 0.10f, 5.5f, "cedar_log"),
                Band<ForestZoneDefinition>("zone.forest_6", "Heartwood", 6, "The oldest trees", new[] { ("cedar_log", 35f), ("maple_log", 25f), ("ironwood_log", 40f) }, "herb", 0.12f, 6f, "cedar_log"),
            };
            var mine = catalogue.Get<SiteDefinition>("site.mine");
            var forest = catalogue.Get<SiteDefinition>("site.forest");
            var bandsChanged = false;
            foreach (var (site, extra) in new[] { (mine, deepLayers), (forest, deepZones) })
            {
                var list = new List<SiteBandDefinition>(site.Bands);
                var changed = false;
                foreach (var b in extra) if (!list.Contains(b)) { list.Add(b); changed = true; }
                if (changed) { site.EditorSetBands(list); EditorUtility.SetDirty(site); bandsChanged = true; }
            }
            var allBands = new List<SiteBandDefinition>(catalogue.Bands);
            foreach (var b in deepLayers) if (!allBands.Contains(b)) { allBands.Add(b); bandsChanged = true; }
            foreach (var b in deepZones) if (!allBands.Contains(b)) { allBands.Add(b); bandsChanged = true; }
            if (bandsChanged)
            {
                catalogue.EditorSetSites(new List<SiteDefinition>(catalogue.Sites), allBands, new List<WorkerDefinition>(catalogue.Workers),
                    new List<WorkerEquipmentDefinition>(catalogue.Equipment));
                EditorUtility.SetDirty(catalogue);
            }

            // ---------------------------------------------------------------- facilities, processing, recipes
            StationDefinition St(string key) => catalogue.Get<StationDefinition>("station." + key);
            foreach (var (key, max) in new[] { ("smelter", 3), ("lumbermill", 3), ("forge", 3), ("workbench", 2) })
            {
                var st = St(key);
                if (st.MaxLevel < max) { st.EditorSetMaxLevel(max); EditorUtility.SetDirty(st); }
            }
            ProcessDefinition Process(string key, string name, StationDefinition f, int level, List<ResourceCost> ins, string output, float seconds)
            {
                var p = Make<ProcessDefinition>(ProductionDir, "process_" + key, "process." + key, name, out var created);
                if (created) { p.EditorSetupProcess(f, level, ins, R(output), 1, seconds); EditorUtility.SetDirty(p); }
                return p;
            }
            var newProcesses = new List<ProcessDefinition>
            {
                Process("smelt_moonsilver", "Smelt moonsilver", St("smelter"), 3, M(("moonsilver_ore", 2)), "moonsilver_ingot", 7f),
                Process("saw_ironwood", "Saw ironwood", St("lumbermill"), 3, M(("ironwood_log", 2)), "ironwood_lumber", 6f),
            };
            var newBlueprints = new List<BlueprintDefinition>();
            var newRecipes = new List<RecipeDefinition>();
            BlueprintDefinition Recipe(string item, StationDefinition st, List<ResourceCost> cost, string reveal, int level)
            {
                var bp = Make<BlueprintDefinition>(ProductionDir, "blueprint_" + item, "blueprint." + item, I(item).DisplayName + " blueprint", out var bpCreated);
                if (bpCreated) { bp.EditorSetupBlueprint(false, reveal); EditorUtility.SetDirty(bp); }
                var r = Make<RecipeDefinition>(ProductionDir, "recipe_" + item, "recipe." + item, I(item).DisplayName, out var rCreated);
                if (rCreated) { r.EditorSetupRecipe(I(item), cost, st, level, bp); EditorUtility.SetDirty(r); }
                newBlueprints.Add(bp);
                newRecipes.Add(r);
                return bp;
            }
            var forge = St("forge");
            var bench = St("workbench");
            var apo = St("apothecary");
            var bpMaul = Recipe("steel_maul", forge, M(("steel_ingot", 4), ("maple_lumber", 1)), "A crushing two-hander for warriors.", 2);
            var bpStiletto = Recipe("steel_stiletto", forge, M(("steel_ingot", 3), ("cedar_lumber", 1)), "A slim steel blade for rogues.", 2);
            var bpBrigandine = Recipe("fur_brigandine", forge, M(("iron_ingot", 2), ("fur", 2), ("rope", 1)), "Warm, light armor that helps on wild routes.", 1);
            var bpStrong = Recipe("strong_tonic", apo, M(("herb", 3), ("arcane_dust", 1)), "A stronger tonic: more survival margin if things go badly.", 2);
            var bpPath = Recipe("pathfinder_draught", apo, M(("herb", 2), ("fur", 1)), "Counters hard-to-find routes when present.", 2);
            var bpTrap = Recipe("trapsense_draught", apo, M(("herb", 2), ("rope", 1)), "Counters traps when present.", 2);
            var bpFocus = Recipe("moonsilver_focus", bench, M(("ironwood_lumber", 3), ("moonsilver_ingot", 2), ("arcane_dust", 1)), "A mage's focus that wards against the arcane.", 2);
            var bpMail = Recipe("moonsilver_mail", forge, M(("moonsilver_ingot", 4), ("steel_ingot", 2), ("fur", 1)), "The finest armor Ravenholm can make.", 3);
            var bpGreatbow = Recipe("ironwood_greatbow", bench, M(("ironwood_lumber", 3), ("steel_ingot", 2), ("rope", 1)), "A heavy bow for the far frontier.", 2);
            var bpBlade = Recipe("moonsilver_blade", forge, M(("moonsilver_ingot", 3), ("steel_ingot", 2), ("maple_lumber", 1)), "A blade worthy of the Beacon.", 3);

            var prodChanged = false;
            var procs = new List<ProcessDefinition>(catalogue.Processes);
            var recs = new List<RecipeDefinition>(catalogue.Recipes);
            var bps = new List<BlueprintDefinition>(catalogue.Blueprints);
            foreach (var x in newProcesses) if (!procs.Contains(x)) { procs.Add(x); prodChanged = true; }
            foreach (var x in newRecipes) if (!recs.Contains(x)) { recs.Add(x); prodChanged = true; }
            foreach (var x in newBlueprints) if (!bps.Contains(x)) { bps.Add(x); prodChanged = true; }
            if (prodChanged)
            {
                catalogue.EditorSetProduction(new List<StationDefinition>(catalogue.Stations), procs, recs, bps, catalogue.StartingState);
                EditorUtility.SetDirty(catalogue);
            }

            // Optional side blueprints from the Trader's 20 % slot (never required by anything).
            var trader = catalogue.TraderStock;
            if (trader != null && trader.SideBlueprints.Count == 0)
            {
                trader.EditorSetSideBlueprints(new List<BlueprintDefinition> { bpPath, bpTrap });
                EditorUtility.SetDirty(trader);
            }

            // ---------------------------------------------------------------- threats and regions
            ThreatDefinition Threat(string key, string name, CounterTag tag, int req, int unmet, int unmetSurv, int armor, string reveal)
            {
                var t = Make<ThreatDefinition>(JobsDir, "threat_" + key, "threat." + key, name, out var created);
                if (created) { t.EditorSetupThreat(tag, req, unmet, unmetSurv, armor, armor > 0 ? 2 : 0, armor > 0 ? 2 : 0, reveal); EditorUtility.SetDirty(t); }
                return t;
            }
            ThreatDefinition Existing(string key) => catalogue.Get<ThreatDefinition>("threat." + key);
            var exploration = Existing("exploration");
            var arcane = Existing("arcane");
            var heavy = Existing("heavy_blows");
            var poison = Existing("poison");
            var traps = Existing("traps");
            var approach = Threat("beacon_approach", "The uncharted approach", CounterTag.Exploration, 8, -8, -4, 0,
                "Nobody knows the way up. Chart the Beacon Road first, or bring a superb guide.");
            var wardens = Threat("pass_wardens", "The pass wardens", CounterTag.HeavyBlows, 0, -8, -3, 16,
                "Armored wardens hold the pass. Secure it first; no armor alone turns them.");
            var flame = Threat("pale_flame", "The Pale Flame", CounterTag.Arcane, 10, -6, -3, 0,
                "An arcane fire at the Beacon. It needs 10 arcane counter: a mage or ward gear plus a Warding Draught.");

            RegionDefinition Region(string key, string name, int order, string theme, string rumour)
            {
                var r = Make<RegionDefinition>(CampaignDir, "region_" + key, "region." + key, name, out var created);
                if (created) { r.EditorSetupRegion(order, theme, rumour); EditorUtility.SetDirty(r); }
                return r;
            }
            var frontier = Region("frontier_paths", "Frontier Paths", 0, "Reliable preparation; opens coal and maple.", "The roads around Ravenholm.");
            var hollow = Region("hollow_march", "Hollow March", 1, "Poison, traps and magical components.", "Travellers speak of fens, warrens and witch-lights to the east.");
            var crown = Region("crownward_ruins", "Crownward Ruins", 2, "Deliberate counters and advanced gear.", "Beyond the March, old ruins guard the road to the Beacon.");

            // ---------------------------------------------------------------- quests
            QuestDefinition ExistingQuest(string key) => catalogue.Get<QuestDefinition>("quest." + key);
            QuestDefinition Quest(string key, string name, RegionDefinition region, int d, float seconds, int gold, int prestige, string brief,
                params (ThreatDefinition t, bool known)[] threats)
            {
                var q = Make<QuestDefinition>(JobsDir, "quest_" + key, "quest." + key, name, out var created);
                if (!created) return q;
                var list = new List<QuestThreat>();
                foreach (var (t, known) in threats) list.Add(new QuestThreat { threat = t, knownAtStart = known });
                q.EditorSetupQuest(d, seconds, list, gold, true, false, prestige, brief);
                q.EditorSetRegion(region);
                EditorUtility.SetDirty(q);
                return q;
            }
            var fur = Quest("fur_trappers_camp", "Fur Trappers' Camp", hollow, 24, 210f, 56, 45, "A trappers' camp in the hills wants an escort through unmapped country.", (exploration, true));
            var mire = Quest("mire_crossing", "Mire Crossing", hollow, 25, 210f, 58, 45, "A causeway over the mire, poisonous and rigged by smugglers.", (poison, true), (traps, false));
            var ford = Quest("bandit_ford", "Bandit Ford", hollow, 25, 210f, 60, 45, "Armored bandits hold the ford.", (heavy, true));
            var warren = Quest("hollow_warren", "Hollow Warren", hollow, 26, 240f, 62, 50, "A maze of tunnels under the March, full of old snares.", (traps, true), (exploration, false));
            var witch = Quest("witchlight_fen", "Witchlight Fen", hollow, 27, 240f, 66, 50, "Lights over the fen lure travellers into poison water.", (arcane, true), (poison, true));
            var archive = Quest("sunken_archive", "Sunken Archive", hollow, 28, 270f, 70, 55, "A drowned library: wards, and worse.", (arcane, true), (traps, false));
            var gate = Quest("crownward_gate", "Crownward Gate", crown, 29, 270f, 76, 60, "The ruined gate of the Crownward road.", (heavy, true), (traps, true));
            var hunt = Quest("ironwood_hunt", "Ironwood Hunt", crown, 28, 270f, 74, 60, "Hunt the beasts that nest in the far ironwood.", (exploration, true), (heavy, false));
            var barracks = Quest("ashen_barracks", "Ashen Barracks", crown, 30, 300f, 82, 65, "A burned barracks where something still keeps watch.", (heavy, true), (arcane, true));
            var road = Quest("beacon_road", "Chart the Beacon Road", crown, 29, 300f, 90, 70, "A capable explorer must find a way up to the Beacon.", (exploration, true), (traps, true));
            var pass = Quest("beacon_pass", "Hold the Beacon Pass", crown, 30, 300f, 95, 70, "A seasoned fighter must clear the pass for the final climb.", (heavy, true), (arcane, true));
            var beacon = Quest("beacon_expedition", "The Beacon Expedition", crown, 32, 360f, 150, 80,
                "Relight the Beacon. Send an elite contractor with advanced gear and a counter to the Pale Flame.",
                (approach, true), (wardens, true), (flame, true));

            var all = new List<QuestDefinition> { fur, mire, ford, warren, witch, archive, gate, hunt, barracks, road, pass, beacon };
            BlueprintDefinition B(string key) => AssetDatabase.LoadAssetAtPath<BlueprintDefinition>(ProductionDir + "blueprint_" + key + ".asset");
            void Progression(QuestDefinition q, QuestDefinition[] prereqs, int gate2, int tier, List<ResourceCost> success, List<ResourceCost> great,
                BlueprintDefinition blueprint, List<ResourceCost> sub, string rare, float chance)
            {
                var configured = q.SuccessMaterials.Count > 0 || q.FirstSuccessBlueprint != null || q.Prerequisites.Count > 0 || q.MapTier > 0;
                if (configured) return;
                q.EditorSetupProgression(new List<QuestDefinition>(prereqs), gate2, tier, success, great, new List<ResourceCost>(), blueprint, sub,
                    rare != null ? R(rare) : null, chance);
                EditorUtility.SetDirty(q);
            }
            var briar = ExistingQuest("briar_track");
            var fen = ExistingQuest("fen_path");
            var quarry = ExistingQuest("quarry_road");
            var tower = ExistingQuest("fallen_watchtower");
            var chamber = ExistingQuest("shrine_chamber");
            Progression(fur, new[] { briar }, 30, 3, M(("fur", 2), ("rope", 2)), M(("fur", 1)), bpBrigandine, M(("fur", 2)), "gem", 0.5f);
            Progression(mire, new[] { fen }, 30, 3, M(("herb", 3), ("rope", 1)), M(("herb", 2)), bpStrong, M(("herb", 3)), "gem", 0.5f);
            Progression(ford, new[] { quarry }, 30, 3, M(("steel_ingot", 2), ("rope", 1)), M(("steel_ingot", 1)), bpMaul, M(("steel_ingot", 2)), "gem", 0.5f);
            Progression(warren, new[] { tower }, 30, 3, M(("steel_ingot", 2)), M(("rope", 1)), bpStiletto, M(("steel_ingot", 2)), "gem", 0.5f);
            Progression(witch, new[] { chamber, mire }, 0, 4, M(("arcane_dust", 2), ("herb", 2)), M(("arcane_dust", 1)), null, M(), "gem", 0.5f);
            Progression(archive, new[] { witch }, 0, 5, M(("arcane_dust", 2), ("moonsilver_ingot", 1)), M(("arcane_dust", 1)), bpFocus, M(("moonsilver_ingot", 2)), "gem", 0.5f);
            Progression(gate, new[] { ford, warren }, 45, 5, M(("moonsilver_ingot", 2)), M(("steel_ingot", 2)), bpMail, M(("moonsilver_ingot", 2)), "gem", 0.5f);
            Progression(hunt, new[] { gate }, 0, 6, M(("ironwood_lumber", 2), ("fur", 1)), M(("fur", 1)), bpGreatbow, M(("ironwood_lumber", 2)), "gem", 0.5f);
            Progression(barracks, new[] { gate, archive }, 0, 6, M(("moonsilver_ingot", 2), ("steel_ingot", 2)), M(("moonsilver_ingot", 1)), bpBlade, M(("moonsilver_ingot", 2)), "gem", 0.5f);
            Progression(road, new[] { barracks }, 0, 7, M(("moonsilver_ingot", 2)), M(("arcane_dust", 1)), null, M(), "gem", 0.5f);
            Progression(pass, new[] { barracks }, 0, 7, M(("moonsilver_ingot", 2)), M(("arcane_dust", 1)), null, M(), "gem", 0.5f);
            Progression(beacon, new[] { road, pass }, 0, 8, M(("moonsilver_ingot", 4), ("arcane_dust", 2)), M(), null, M(), null, 0f);

            // Campaign chain (GDD 14): the breadcrumb follows the finale's ancestors in a valid order.
            void Campaign(QuestDefinition q, int order, bool finale = false, List<QuestPreparation> preps = null, int minLevel = 0, int rep = 0,
                int power = 0, int protection = 0, bool potion = false, bool repeatable = true)
            {
                if (q.CampaignStep > 0) return;
                q.EditorSetupCampaign(order, finale, preps ?? new List<QuestPreparation>(), minLevel, rep, power, protection, potion, repeatable);
                EditorUtility.SetDirty(q);
            }
            var step = 1;
            foreach (var key in new[] { "roadside_patrol", "briar_track", "old_shrine", "quarry_road", "fallen_watchtower", "fen_path", "shrine_chamber" })
                Campaign(ExistingQuest(key), step++);
            foreach (var q in new[] { ford, warren, mire, witch, archive, gate, barracks }) Campaign(q, step++);
            Campaign(road, step++, preps: new List<QuestPreparation> { new QuestPreparation { quest = beacon, threat = approach, note = "Route charted" } },
                minLevel: 4, repeatable: false);
            Campaign(pass, step++, preps: new List<QuestPreparation> { new QuestPreparation { quest = beacon, threat = wardens, note = "Pass secured" } },
                minLevel: 4, repeatable: false);
            Campaign(beacon, step, finale: true, minLevel: 6, rep: 70, power: 13, protection: 10, potion: true, repeatable: false);

            // Existing Frontier jobs join their region.
            foreach (var q in catalogue.Quests)
                if (q != null && q.Region == null) { q.EditorSetRegion(frontier); EditorUtility.SetDirty(q); }

            var quests = new List<QuestDefinition>(catalogue.Quests);
            var threats = new List<ThreatDefinition>(catalogue.Threats);
            var jobsChanged = false;
            foreach (var q in all) if (!quests.Contains(q)) { quests.Add(q); jobsChanged = true; }
            foreach (var t in new[] { approach, wardens, flame }) if (!threats.Contains(t)) { threats.Add(t); jobsChanged = true; }
            if (jobsChanged) { catalogue.EditorSetQuests(quests, threats); EditorUtility.SetDirty(catalogue); }
            if (catalogue.Regions.Count == 0) { catalogue.EditorSetRegions(new List<RegionDefinition> { frontier, hollow, crown }); EditorUtility.SetDirty(catalogue); }

            // ---------------------------------------------------------------- level 3 facilities
            UpgradeDefinition Up(string key, string name, StationDefinition st, int level, int gold, List<ResourceCost> mats, string effect,
                UpgradeDefinition reqUp, SiteDefinition reqSite = null, int reqBand = 0, QuestDefinition reqQuest = null)
            {
                var u = Make<UpgradeDefinition>(EconomyDir, "upgrade_" + key, "upgrade." + key, name, out var created);
                if (created) { u.EditorSetupUpgrade(UpgradeKind.StationLevel, st, null, level, gold, mats, effect, reqUp, reqSite, reqBand, reqQuest, 0); EditorUtility.SetDirty(u); }
                return u;
            }
            UpgradeDefinition Old(string key) => catalogue.Get<UpgradeDefinition>("upgrade." + key);
            var newUpgrades = new[]
            {
                Up("smelter_3", "Smelter 3", St("smelter"), 3, 90, M(("steel_ingot", 8), ("maple_lumber", 6)), "Unlocks moonsilver; basic batch time ×0.85 again", Old("smelter_2"), mine, 4),
                Up("lumbermill_3", "Lumbermill 3", St("lumbermill"), 3, 80, M(("steel_ingot", 8), ("maple_lumber", 8)), "Unlocks ironwood sawing; basic batch time ×0.85 again", Old("lumbermill_2"), forest, 4),
                Up("forge_3", "Forge 3", forge, 3, 110, M(("steel_ingot", 6), ("moonsilver_ingot", 4)), "Enables moonsilver arms and armor", Old("forge_2"), reqQuest: gate),
                Up("workbench_2", "Workbench 2", bench, 2, 60, M(("steel_ingot", 6), ("maple_lumber", 6)), "Enables ironwood and moonsilver bows and staves", null, reqQuest: warren),
            };
            var ups = new List<UpgradeDefinition>(catalogue.Upgrades);
            var upChanged = false;
            foreach (var u in newUpgrades) if (!ups.Contains(u)) { ups.Add(u); upChanged = true; }
            if (upChanged) { catalogue.EditorSetEconomy(catalogue.TraderStock, ups); EditorUtility.SetDirty(catalogue); }
            AssetDatabase.SaveAssets();
        }
    }
}
