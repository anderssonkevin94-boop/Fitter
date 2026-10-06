using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ravenholm.Content;
using Ravenholm.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ravenholm.EditorTools
{
    /// <summary>
    /// Idempotent Phase 0 project setup. Creates missing assets only: content you have
    /// edited in the Inspector is never overwritten. Scenes are generated once; regenerate
    /// them explicitly with "Rebuild Sandbox Scenes" (that DOES overwrite manual scene edits).
    ///
    /// Batch mode: -executeMethod Ravenholm.EditorTools.ProjectSetup.RunFromCommandLine [-rsc-rebuild-scenes]
    /// </summary>
    public static class ProjectSetup
    {
        public const string Root = "Assets/Ravenholm/";
        public const string ConfigDir = Root + "Content/Config/";
        public const string ResourceDir = Root + "Content/GameResources/";
        public const string ItemDir = Root + "Content/Items/";
        public const string TileDir = Root + "Tiles/";
        public const string SceneDir = Root + "Scenes/";
        public const string BootstrapScene = SceneDir + "Bootstrap.unity";
        public const string SandboxScene = SceneDir + "DebugSandbox.unity";
        public const int BlockerLayerIndex = 8;
        public const string BundleId = "com.kevinandersson.ravenholmsupplyco";
        public const string MinIosVersion = "15.0";
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        [MenuItem("Ravenholm/Setup/Run Phase 0 Setup (safe, creates missing only)", priority = 20)]
        public static void RunFromMenu() => RunAll(rebuildScenes: false);

        [MenuItem("Ravenholm/Setup/Rebuild Sandbox Scenes (overwrites scene edits)", priority = 21)]
        public static void RebuildScenesFromMenu()
        {
            if (!EditorUtility.DisplayDialog("Rebuild sandbox scenes?",
                    "Bootstrap and DebugSandbox will be regenerated. Manual edits to those two scenes are lost.", "Rebuild", "Cancel"))
                return;
            RunAll(rebuildScenes: true);
        }

        public static void RunFromCommandLine()
        {
            try
            {
                var rebuild = Environment.GetCommandLineArgs().Contains("-rsc-rebuild-scenes");
                var ok = RunAll(rebuild);
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static bool RunAll(bool rebuildScenes)
        {
            ConfigurePlayer();
            EnsureSortingLayers();
            EnsurePhysicsLayer(BlockerLayerIndex, PlayerMotor.BlockerLayerName);
            if (!EnsureTmpEssentials())
            {
                Debug.LogError("[Setup] TextMesh Pro essentials are missing. Batch mode cannot import packages from a method; run Unity once with " +
                               "-importPackage \"<project>/Library/PackageCache/com.unity.ugui@*/Package Resources/TMP Essential Resources.unitypackage\" " +
                               "(Tools/setup.sh does this), or use Window > TextMeshPro > Import TMP Essential Resources, then rerun setup.");
                return false;
            }
            ReimportArt();

            var catalogue = EnsureContent();
            EnsureSiteContent(catalogue);
            EnsureProductionContent(catalogue);
            var settings = EnsureAsset<SandboxSettings>(ConfigDir + "SandboxSettings.asset");
            var theme = EnsureUiTheme();
            var balance = EnsureAsset<BalanceProfile>(ConfigDir + "BalanceProfile.asset");
            var boot = EnsureAsset<BootConfig>(ConfigDir + "BootConfig.asset");
            if (boot.Catalogue == null || boot.SandboxSettings == null || boot.UiTheme == null || boot.Balance == null)
            {
                boot.EditorSetup(boot.Catalogue != null ? boot.Catalogue : catalogue,
                    boot.SandboxSettings != null ? boot.SandboxSettings : settings,
                    boot.UiTheme != null ? boot.UiTheme : theme,
                    boot.Balance != null ? boot.Balance : balance);
                EditorUtility.SetDirty(boot);
            }
            EnsureTiles();
            AssetDatabase.SaveAssets();

            if (rebuildScenes || !File.Exists(BootstrapScene)) SandboxSceneBuilder.BuildBootstrap(ConfigDir + "BootConfig.asset", BootstrapScene);
            if (rebuildScenes || !File.Exists(SandboxScene)) SandboxSceneBuilder.BuildSandbox(ConfigDir + "SandboxSettings.asset", SandboxScene);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapScene, true),
                new EditorBuildSettingsScene(SandboxScene, true),
            };
            AssetDatabase.SaveAssets();

            // Re-load from disk: building scenes unloads assets, so earlier in-memory references can be stale.
            var report = ContentValidationMenu.ValidateProjectCatalogue();
            if (report.IsValid) Debug.Log("[Setup] Done. " + report);
            else Debug.LogError("[Setup] Content invalid. " + report);
            return report.IsValid;
        }

        // ------------------------------------------------------------------ player settings

        public static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Kevin Andersson";
            PlayerSettings.productName = "Ravenholm Supply Co";   // working title
            PlayerSettings.bundleVersion = "0.0.1";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);
            PlayerSettings.iOS.buildNumber = "1";
            PlayerSettings.iOS.targetOSVersionString = MinIosVersion;
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
            PlayerSettings.iOS.requiresFullScreen = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.statusBarHidden = true;
            PlayerSettings.runInBackground = false;
            // Portrait window for desktop/editor builds so layout matches a phone.
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 590;
            PlayerSettings.defaultScreenHeight = 1280;
            PlayerSettings.resizableWindow = true;
        }

        public static void EnsureSortingLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("m_SortingLayers");
            // Unity draws later sorting layers on top. Ground goes before Default, Overlay after it.
            EnsureSortingLayer(layers, "Ground", insertAtStart: true);
            EnsureSortingLayer(layers, "Overlay", insertAtStart: false);
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsurePhysicsLayer(int index, string name)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            var slot = layers.GetArrayElementAtIndex(index);
            if (slot.stringValue == name) return;
            if (!string.IsNullOrEmpty(slot.stringValue))
                throw new InvalidOperationException($"Physics layer {index} is already '{slot.stringValue}', expected '{name}'.");
            slot.stringValue = name;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureSortingLayer(SerializedProperty layers, string name, bool insertAtStart)
        {
            for (var i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == name) return;
            var index = insertAtStart ? 0 : layers.arraySize;
            layers.InsertArrayElementAtIndex(index);
            var e = layers.GetArrayElementAtIndex(index);
            e.FindPropertyRelative("name").stringValue = name;
            e.FindPropertyRelative("uniqueID").intValue = name.GetHashCode() & 0x7FFFFFFF;
        }

        private static bool EnsureTmpEssentials()
        {
            if (File.Exists(TmpSettingsPath)) return true;
            var pkg = Directory.GetDirectories("Library/PackageCache", "com.unity.ugui*").Select(d => Path.Combine(d, "Package Resources", "TMP Essential Resources.unitypackage")).FirstOrDefault(File.Exists);
            if (pkg == null) throw new FileNotFoundException("TMP Essential Resources.unitypackage not found in the ugui package.");
            if (Application.isBatchMode) return false;   // async import never completes before a batch method exits
            AssetDatabase.ImportPackage(Path.GetFullPath(pkg), false);
            AssetDatabase.Refresh();
            return File.Exists(TmpSettingsPath);
        }

        private static void ReimportArt()
        {
            PlaceholderSpriteImporter.InvalidateCache();
            AssetDatabase.ImportAsset(PlaceholderSpriteImporter.ArtRoot.TrimEnd('/'), ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        }

        // ------------------------------------------------------------------ content

        private struct ResourceSpec
        {
            public string Key, Name;
            public ResourceCategory Category;
            public ReceiverType Receiver;
        }

        private static readonly ResourceSpec[] Resources =
        {
            new ResourceSpec { Key = "iron_ore", Name = "Iron Ore", Category = ResourceCategory.RawMetal, Receiver = ReceiverType.Smelter },
            new ResourceSpec { Key = "copper_ore", Name = "Copper Ore", Category = ResourceCategory.RawMetal, Receiver = ReceiverType.Smelter },
            new ResourceSpec { Key = "coal", Name = "Coal", Category = ResourceCategory.RawMetal, Receiver = ReceiverType.Smelter },
            new ResourceSpec { Key = "gem", Name = "Gem", Category = ResourceCategory.Rare, Receiver = ReceiverType.Smelter },
            new ResourceSpec { Key = "cedar_log", Name = "Cedar Log", Category = ResourceCategory.RawTimber, Receiver = ReceiverType.Lumbermill },
            new ResourceSpec { Key = "maple_log", Name = "Maple Log", Category = ResourceCategory.RawTimber, Receiver = ReceiverType.Lumbermill },
            new ResourceSpec { Key = "herb", Name = "Meadow Herb", Category = ResourceCategory.Herb, Receiver = ReceiverType.Apothecary },
            new ResourceSpec { Key = "iron_ingot", Name = "Iron Ingot", Category = ResourceCategory.Processed, Receiver = ReceiverType.None },
            new ResourceSpec { Key = "copper_ingot", Name = "Copper Ingot", Category = ResourceCategory.Processed, Receiver = ReceiverType.None },
            new ResourceSpec { Key = "steel_ingot", Name = "Steel Ingot", Category = ResourceCategory.Processed, Receiver = ReceiverType.None },
            new ResourceSpec { Key = "cedar_lumber", Name = "Cedar Lumber", Category = ResourceCategory.Processed, Receiver = ReceiverType.None },
            new ResourceSpec { Key = "maple_lumber", Name = "Maple Lumber", Category = ResourceCategory.Processed, Receiver = ReceiverType.None },
            new ResourceSpec { Key = "rope", Name = "Rope", Category = ResourceCategory.External, Receiver = ReceiverType.None },
            new ResourceSpec { Key = "fur", Name = "Fur", Category = ResourceCategory.External, Receiver = ReceiverType.None },
            new ResourceSpec { Key = "arcane_dust", Name = "Arcane Dust", Category = ResourceCategory.External, Receiver = ReceiverType.None },
        };

        private struct ItemSpec
        {
            public string Key, Name;
            public ItemCategory Category;
            public int Power, Protection, Strength, BuyValue;
            public WeaponTag Tag;
            public PotionEffect Effect;
        }

        // GDD 9 starter catalogue (balance starting points). Trader buy values per GDD 15: 1-3 gold.
        private static readonly ItemSpec[] Items =
        {
            new ItemSpec { Key = "iron_sword", Name = "Iron Sword", Category = ItemCategory.Weapon, Power = 8, Tag = WeaponTag.Blade, BuyValue = 2 },
            new ItemSpec { Key = "cedar_bow", Name = "Cedar Bow", Category = ItemCategory.Weapon, Power = 7, Tag = WeaponTag.Bow, BuyValue = 2 },
            new ItemSpec { Key = "iron_dagger", Name = "Iron Dagger", Category = ItemCategory.Weapon, Power = 7, Tag = WeaponTag.LightBlade, BuyValue = 1 },
            new ItemSpec { Key = "cedar_staff", Name = "Cedar Staff", Category = ItemCategory.Weapon, Power = 7, Tag = WeaponTag.Staff, BuyValue = 2 },
            new ItemSpec { Key = "riveted_coat", Name = "Riveted Coat", Category = ItemCategory.Armor, Protection = 6, BuyValue = 3 },
            new ItemSpec { Key = "recovery_tonic", Name = "Recovery Tonic", Category = ItemCategory.Potion, Effect = PotionEffect.SurvivalMargin, Strength = 4, BuyValue = 1 },
            // Phase 3 test unlock (advanced, GDD 9): Power 10, arcane counter +4.
            new ItemSpec { Key = "copper_ward_staff", Name = "Copper Ward Staff", Category = ItemCategory.Weapon, Power = 10, Tag = WeaponTag.Staff, BuyValue = 3 },
        };

        private static ContentCatalogue EnsureContent()
        {
            var resources = new List<ResourceDefinition>();
            foreach (var spec in Resources)
            {
                var path = ResourceDir + "resource_" + spec.Key + ".asset";
                var created = !File.Exists(path);
                var def = EnsureAsset<ResourceDefinition>(path);
                if (created)
                {
                    def.EditorInitialize("resource." + spec.Key, spec.Name);
                    def.EditorSetup(spec.Category, spec.Receiver,
                        LoadSprite("Icons/Resources/icon_" + spec.Key), LoadSprite("Stacks/stack_" + spec.Key));
                    EditorUtility.SetDirty(def);
                }
                resources.Add(def);
            }

            var items = new List<ItemDefinition>();
            foreach (var spec in Items)
            {
                var path = ItemDir + "item_" + spec.Key + ".asset";
                var created = !File.Exists(path);
                var def = EnsureAsset<ItemDefinition>(path);
                if (created)
                {
                    def.EditorInitialize("item." + spec.Key, spec.Name);
                    def.EditorSetup(spec.Category, spec.Power, spec.Protection, spec.Tag, spec.Effect, spec.Strength, spec.BuyValue,
                        LoadSprite("Icons/Items/icon_" + spec.Key), LoadSprite("Stacks/stack_" + spec.Key));
                    EditorUtility.SetDirty(def);
                }
                items.Add(def);
            }

            var cataloguePath = ConfigDir + "ContentCatalogue.asset";
            var catalogueCreated = !File.Exists(cataloguePath);
            var catalogue = EnsureAsset<ContentCatalogue>(cataloguePath);
            if (catalogueCreated || catalogue.Resources.Count == 0 && catalogue.Items.Count == 0)
            {
                catalogue.EditorSet(resources, items);
                EditorUtility.SetDirty(catalogue);
            }
            else
            {
                // Later phases add definitions; append them without disturbing existing entries.
                var r = new List<ResourceDefinition>(catalogue.Resources);
                var i = new List<ItemDefinition>(catalogue.Items);
                var changed = false;
                foreach (var d in resources) if (!r.Contains(d)) { r.Add(d); changed = true; }
                foreach (var d in items) if (!i.Contains(d)) { i.Add(d); changed = true; }
                if (changed) { catalogue.EditorSet(r, i); EditorUtility.SetDirty(catalogue); }
            }
            var ward = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDir + "item_copper_ward_staff.asset");
            if (ward != null && ward.CounterTag == CounterTag.None) { ward.EditorSetCounter(CounterTag.Arcane, 4); EditorUtility.SetDirty(ward); }
            return catalogue;
        }

        public const string SiteDir = Root + "Content/Sites/";
        public const string WorkerDir = Root + "Content/Workers/";

        /// <summary>Phase 2 content (GDD 5–7 balance starting points). Creates missing assets only.</summary>
        private static void EnsureSiteContent(ContentCatalogue catalogue)
        {
            ResourceDefinition R(string key) => catalogue.Get<ResourceDefinition>("resource." + key);

            var tints = new Dictionary<string, Color32>
            {
                { "iron_ore", new Color32(150, 156, 168, 255) }, { "copper_ore", new Color32(212, 128, 72, 255) },
                { "coal", new Color32(54, 54, 62, 255) }, { "gem", new Color32(96, 200, 220, 255) },
                { "cedar_log", new Color32(176, 110, 70, 255) }, { "maple_log", new Color32(214, 160, 108, 255) },
                { "herb", new Color32(110, 176, 84, 255) },
            };
            foreach (var kv in tints)
            {
                var r = R(kv.Key);
                if (r.MapTint != new Color(0.6f, 0.6f, 0.6f, 1f)) continue;   // keep Inspector edits
                r.EditorSetTint(kv.Value);
                EditorUtility.SetDirty(r);
            }

            WorkerEquipmentDefinition Equip(string key, string name, EquipmentSlot slot, int tier, int value, int gold, string costRes, int costQty, string gate)
            {
                var path = WorkerDir + "equipment_" + key + ".asset";
                var created = !File.Exists(path);
                var e = EnsureAsset<WorkerEquipmentDefinition>(path);
                if (!created) return e;
                var cost = new List<ResourceCost>();
                if (costRes != null) cost.Add(new ResourceCost { resource = R(costRes), quantity = costQty });
                e.EditorInitialize("equipment." + key, name);
                e.EditorSetupEquipment(slot, tier, value, gold, cost, gate);
                EditorUtility.SetDirty(e);
                return e;
            }
            var equipment = new List<WorkerEquipmentDefinition>
            {
                Equip("tool_basic", "Basic tool", EquipmentSlot.Tool, 1, 10, 0, null, 0, "Start (permanent)"),
                Equip("tool_iron", "Iron tool", EquipmentSlot.Tool, 2, 16, 30, "iron_ingot", 6, "Site equipment level 2"),
                Equip("tool_tempered", "Tempered tool", EquipmentSlot.Tool, 3, 24, 80, "steel_ingot", 8, "Site equipment level 3"),
                Equip("pack_basic", "Basic pack", EquipmentSlot.Pack, 1, 6, 0, null, 0, "Start (permanent)"),
                Equip("pack_framed", "Framed pack", EquipmentSlot.Pack, 2, 12, 25, "cedar_lumber", 6, "Site equipment level 2"),
                Equip("pack_reinforced", "Reinforced pack", EquipmentSlot.Pack, 3, 18, 65, "maple_lumber", 6, "Site equipment level 3"),
            };

            WorkerDefinition Worker(string key, string name, int seed)
            {
                var path = WorkerDir + "worker_" + key + ".asset";
                var created = !File.Exists(path);
                var w = EnsureAsset<WorkerDefinition>(path);
                if (!created) return w;
                w.EditorInitialize("worker." + key, name);
                w.EditorSetupWorker(seed, equipment[0], equipment[3]);
                EditorUtility.SetDirty(w);
                return w;
            }
            var miner = Worker("dagna", "Dagna Holt", 101);
            var woodcutter = Worker("tobin", "Tobin Ash", 202);

            T Band<T>(string key, string name, int index, string discovery, (string res, float w)[] weights, string rare, float chance, float leg, string baseline)
                where T : SiteBandDefinition
            {
                var path = SiteDir + key.Split('.')[0] + "_" + key.Split('.')[1] + ".asset";
                var created = !File.Exists(path);
                var b = EnsureAsset<T>(path);
                if (!created) return b;
                var list = new List<WeightedResource>();
                foreach (var (res, w) in weights) list.Add(new WeightedResource { resource = R(res), weight = w });
                b.EditorInitialize(key, name);
                b.EditorSetupBand(index, discovery, list, R(rare), chance, 1f, leg, R(baseline), 0.35f);
                EditorUtility.SetDirty(b);
                return b;
            }
            var layers = new List<SiteBandDefinition>
            {
                Band<LayerDefinition>("layer.mine_1", "Upper strata", 1, "Reliable foundation", new[] { ("iron_ore", 80f), ("copper_ore", 20f) }, "gem", 0.01f, 2.5f, "iron_ore"),
                Band<LayerDefinition>("layer.mine_2", "Copper seam", 2, "Better copper access", new[] { ("iron_ore", 65f), ("copper_ore", 35f) }, "gem", 0.015f, 3.0f, "iron_ore"),
                Band<LayerDefinition>("layer.mine_3", "Coal measures", 3, "Steel processing route", new[] { ("iron_ore", 55f), ("copper_ore", 20f), ("coal", 25f) }, "gem", 0.02f, 3.5f, "iron_ore"),
            };
            var zones = new List<SiteBandDefinition>
            {
                Band<ForestZoneDefinition>("zone.forest_1", "Meadow edge", 1, "Easy cedar", new[] { ("cedar_log", 90f), ("maple_log", 10f) }, "herb", 0.04f, 2f, "cedar_log"),
                Band<ForestZoneDefinition>("zone.forest_2", "Old trail", 2, "More maple", new[] { ("cedar_log", 70f), ("maple_log", 30f) }, "herb", 0.06f, 3f, "cedar_log"),
                Band<ForestZoneDefinition>("zone.forest_3", "Shaded hollow", 3, "Rich maple and herbs", new[] { ("cedar_log", 55f), ("maple_log", 45f) }, "herb", 0.08f, 4f, "cedar_log"),
            };

            SiteDefinition Site(string key, string name, SiteType type, List<SiteBandDefinition> bands, WorkerDefinition worker, string staging,
                int units, float baseD, float growth, float round, float kl, float kq)
            {
                var path = SiteDir + "site_" + key + ".asset";
                var created = !File.Exists(path);
                var site = EnsureAsset<SiteDefinition>(path);
                if (!created) return site;
                site.EditorInitialize("site." + key, name);
                site.EditorSetupSite(type, bands, new List<WorkerDefinition> { worker }, staging, units, 6, baseD, growth, round, kl, kq, 0.8f);
                EditorUtility.SetDirty(site);
                return site;
            }
            var mine = Site("mine", "Mine", SiteType.Mine, layers, miner, "container.mine_staging", 24, 240f, 1.22f, 10f, 0.18f, 0.04f);
            var forest = Site("forest", "Forest", SiteType.Forest, zones, woodcutter, "container.forest_staging", 4, 40f, 1.18f, 5f, 0.15f, 0.03f);

            if (catalogue.Sites.Count == 0)
            {
                var bands = new List<SiteBandDefinition>(layers);
                bands.AddRange(zones);
                catalogue.EditorSetSites(new List<SiteDefinition> { mine, forest }, bands, new List<WorkerDefinition> { miner, woodcutter }, equipment);
                EditorUtility.SetDirty(catalogue);
            }
        }

        public const string ProductionDir = Root + "Content/Production/";

        /// <summary>Phase 3 content (GDD 8/9/15 balance starting points). Creates missing assets only.</summary>
        private static void EnsureProductionContent(ContentCatalogue catalogue)
        {
            ResourceDefinition R(string key) => catalogue.Get<ResourceDefinition>("resource." + key);
            ItemDefinition I(string key) => catalogue.Get<ItemDefinition>("item." + key);
            List<ResourceCost> Cost(params (string res, int q)[] c)
            {
                var l = new List<ResourceCost>();
                foreach (var (res, q) in c) l.Add(new ResourceCost { resource = R(res), quantity = q });
                return l;
            }
            T Make<T>(string file, string id, string name, out bool created) where T : ContentDefinition
            {
                var path = ProductionDir + file + ".asset";
                created = !File.Exists(path);
                var a = EnsureAsset<T>(path);
                if (created) a.EditorInitialize(id, name);
                return a;
            }

            StationDefinition Station(string key, string name, string pad)
            {
                var st = Make<StationDefinition>("station_" + key, "station." + key, name, out var created);
                if (created) { st.EditorSetupStation(pad, 6, 1, 2); EditorUtility.SetDirty(st); }
                return st;
            }
            var smelter = Station("smelter", "Smelter", "");
            var mill = Station("lumbermill", "Lumbermill", "");
            var apothecary = Station("apothecary", "Apothecary", "container.apothecary_pad");
            var forge = Station("forge", "Forge", "container.forge_pad");
            var bench = Station("workbench", "Workbench", "container.workbench_pad");

            ProcessDefinition Process(string key, string name, StationDefinition f, int level, List<ResourceCost> ins, string output, float seconds)
            {
                var p = Make<ProcessDefinition>("process_" + key, "process." + key, name, out var created);
                if (created) { p.EditorSetupProcess(f, level, ins, R(output), 1, seconds); EditorUtility.SetDirty(p); }
                return p;
            }
            var processes = new List<ProcessDefinition>
            {
                Process("smelt_iron", "Smelt iron", smelter, 1, Cost(("iron_ore", 2)), "iron_ingot", 4f),
                Process("smelt_copper", "Smelt copper", smelter, 1, Cost(("copper_ore", 2)), "copper_ingot", 4f),
                Process("make_steel", "Make steel", smelter, 2, Cost(("iron_ore", 2), ("coal", 1)), "steel_ingot", 6f),
                Process("saw_cedar", "Saw cedar", mill, 1, Cost(("cedar_log", 2)), "cedar_lumber", 4f),
                Process("saw_maple", "Saw maple", mill, 2, Cost(("maple_log", 2)), "maple_lumber", 5f),
            };

            var blueprints = new List<BlueprintDefinition>();
            var recipes = new List<RecipeDefinition>();
            void Recipe(string item, StationDefinition st, List<ResourceCost> cost, bool starter, string reveal)
            {
                var bp = Make<BlueprintDefinition>("blueprint_" + item, "blueprint." + item, I(item).DisplayName + " blueprint", out var bpCreated);
                if (bpCreated) { bp.EditorSetupBlueprint(starter, reveal); EditorUtility.SetDirty(bp); }
                var r = Make<RecipeDefinition>("recipe_" + item, "recipe." + item, I(item).DisplayName, out var rCreated);
                if (rCreated) { r.EditorSetupRecipe(I(item), cost, st, 1, bp); EditorUtility.SetDirty(r); }
                blueprints.Add(bp);
                recipes.Add(r);
            }
            // GDD 9 starter catalogue (all owned at start, GDD 15).
            Recipe("iron_sword", forge, Cost(("iron_ingot", 3), ("cedar_lumber", 1)), true, "A dependable blade for warriors.");
            Recipe("cedar_bow", bench, Cost(("cedar_lumber", 3), ("iron_ingot", 1)), true, "A light bow for rangers.");
            Recipe("iron_dagger", forge, Cost(("iron_ingot", 2), ("cedar_lumber", 1)), true, "A quick blade for rogues.");
            Recipe("cedar_staff", bench, Cost(("cedar_lumber", 3), ("copper_ingot", 1)), true, "A plain staff for mages.");
            Recipe("riveted_coat", forge, Cost(("iron_ingot", 3), ("cedar_lumber", 2)), true, "Basic armor for any class.");
            Recipe("recovery_tonic", apothecary, Cost(("herb", 2)), true, "Adds survival margin once if things go badly.");
            // The one advanced recipe for Phase 3 testing (normally from the Old Shrine's first success).
            Recipe("copper_ward_staff", bench, Cost(("cedar_lumber", 3), ("copper_ingot", 3)), false, "A staff that wards against arcane threats.");

            var startPath = ConfigDir + "StartingState.asset";
            var startCreated = !File.Exists(startPath);
            var start = EnsureAsset<StartingState>(startPath);
            if (startCreated)
            {
                start.EditorSetup(Cost(("iron_ingot", 6), ("cedar_lumber", 6), ("copper_ingot", 1), ("herb", 2)));
                EditorUtility.SetDirty(start);
            }

            if (catalogue.Stations.Count == 0)
            {
                catalogue.EditorSetProduction(new List<StationDefinition> { smelter, mill, apothecary, forge, bench }, processes, recipes, blueprints, start);
                EditorUtility.SetDirty(catalogue);
            }
        }

        private static UiTheme EnsureUiTheme()
        {
            var path = ConfigDir + "UiTheme.asset";
            var created = !File.Exists(path);
            var t = EnsureAsset<UiTheme>(path);
            // Fields added in later phases are filled in without touching edited ones.
            if (t.joystickRing == null) { t.joystickRing = LoadSprite("UI/ui_joystick_ring"); EditorUtility.SetDirty(t); }
            if (t.joystickKnob == null) { t.joystickKnob = LoadSprite("UI/ui_joystick_knob"); EditorUtility.SetDirty(t); }
            if (t.treeIcon == null) { t.treeIcon = LoadSprite("Nature/tree_cedar"); EditorUtility.SetDirty(t); }
            if (!created && t.panel != null) return t;
            t.panel = LoadSprite("UI/ui_panel");
            t.panelDark = LoadSprite("UI/ui_panel_dark");
            t.button = LoadSprite("UI/ui_button");
            t.chip = LoadSprite("UI/ui_chip");
            t.roundButton = LoadSprite("UI/ui_round_button");
            t.coin = LoadSprite("UI/Icons/ic_coin");
            t.reputation = LoadSprite("UI/Icons/ic_star");
            t.carry = LoadSprite("UI/Icons/ic_bag");
            t.check = LoadSprite("UI/Icons/ic_check");
            t.warning = LoadSprite("UI/Icons/ic_warning");
            t.locked = LoadSprite("UI/Icons/ic_lock");
            t.timer = LoadSprite("UI/Icons/ic_clock");
            t.unknown = LoadSprite("UI/Icons/ic_unknown");
            t.interact = LoadSprite("UI/Icons/ic_hand");
            t.settings = LoadSprite("UI/Icons/ic_gear");
            EditorUtility.SetDirty(t);
            return t;
        }

        public static Dictionary<string, TileBase> EnsureTiles()
        {
            var result = new Dictionary<string, TileBase>();
            foreach (var key in new[] { "grass_a", "grass_b", "path", "plaza", "dirt", "forest_floor" })
            {
                var path = TileDir + "tile_" + key + ".asset";
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<Tile>();
                    tile.sprite = LoadSprite("Tiles/tile_" + key);
                    tile.colliderType = Tile.ColliderType.None;
                    EnsureFolder(TileDir);
                    AssetDatabase.CreateAsset(tile, path);
                }
                result[key] = tile;
            }
            return result;
        }

        // ------------------------------------------------------------------ helpers

        public static Sprite LoadSprite(string relativeNoExt)
        {
            var path = PlaceholderSpriteImporter.ArtRoot + relativeNoExt + ".png";
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s == null) throw new FileNotFoundException("Placeholder sprite missing (was the art imported?): " + path);
            return s;
        }

        private static T EnsureAsset<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            EnsureFolder(Path.GetDirectoryName(path));
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
