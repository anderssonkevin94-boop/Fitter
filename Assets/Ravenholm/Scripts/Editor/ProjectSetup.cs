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
    public static partial class ProjectSetup
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
            EnsureJobContent(catalogue);
            EnsureEconomyContent(catalogue);
            ApplySoloPlay(catalogue);
            EnsureCampaignContent(catalogue);
            EnsureTutorialContent(catalogue);
            EnsurePostgameContent(catalogue);
            EnsureKnowledgeContent(catalogue);
            ApplyBalanceRevisions(catalogue);
            var settings = EnsureAsset<SandboxSettings>(ConfigDir + "SandboxSettings.asset");
            var theme = EnsureUiTheme();
            EnsureOreBlocks();
            EnsureScaffoldMaterials();
            EnsureSupplierAnimations();
            EnsureStackSteps();
            EnsureOutlines(settings);
            EnsureStoreOwner();
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

        /// <summary>
        /// Solo play (2026-10-11): the supplier is the only one working the sites, so there is no starting crew, no
        /// recruits and no crew expansion. Removes them from existing content too (idempotent).
        /// </summary>
        private static void ApplySoloPlay(ContentCatalogue catalogue)
        {
            foreach (var site in catalogue.Sites)
            {
                if (site == null || site.Workers.Count == 0 && site.Recruits.Count == 0) continue;
                site.EditorSetWorkers(new List<WorkerDefinition>());
                site.EditorSetRecruits(new List<WorkerDefinition>());
                EditorUtility.SetDirty(site);
            }
            if (catalogue.Workers.Count > 0)
            {
                catalogue.EditorSetSites(new List<SiteDefinition>(catalogue.Sites), new List<SiteBandDefinition>(catalogue.Bands), new List<WorkerDefinition>(),
                    new List<WorkerEquipmentDefinition>(catalogue.Equipment));
                EditorUtility.SetDirty(catalogue);
            }
            var upgrades = new List<UpgradeDefinition>();
            var dropped = false;
            foreach (var u in catalogue.Upgrades)
            {
                if (u != null && u.Kind == UpgradeKind.CrewExpansion) { dropped = true; continue; }
                upgrades.Add(u);
            }
            if (dropped)
            {
                catalogue.EditorSetEconomy(catalogue.TraderStock, upgrades);
                EditorUtility.SetDirty(catalogue);
            }
            foreach (var key in new[] { "dagna", "tobin", "mara", "osk" })
                if (File.Exists(WorkerDir + "worker_" + key + ".asset")) AssetDatabase.DeleteAsset(WorkerDir + "worker_" + key + ".asset");
            foreach (var key in new[] { "mine_crew", "forest_crew" })
                if (File.Exists(EconomyDir + "upgrade_" + key + ".asset")) AssetDatabase.DeleteAsset(EconomyDir + "upgrade_" + key + ".asset");
        }

        public static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Kevin Andersson";
            PlayerSettings.productName = "Ravenholm Supply Co";   // working title
            PlayerSettings.bundleVersion = "0.9.0";   // Phase 9 release candidate line
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);
            PlayerSettings.iOS.buildNumber = "9";
            PlayerSettings.iOS.targetOSVersionString = MinIosVersion;
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
            PlayerSettings.iOS.requiresFullScreen = true;
            // Placeholder app icon (original, generated; see PROVENANCE.md). Unity derives every iOS size from it.
            const string iconPath = "Assets/Ravenholm/Art/AppIcon/app_icon.png";
            if (AssetImporter.GetAtPath(iconPath) is TextureImporter ti &&
                (ti.textureCompression != TextureImporterCompression.Uncompressed || ti.mipmapEnabled))
            {
                ti.textureCompression = TextureImporterCompression.Uncompressed;   // icons must stay crisp
                ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
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
            // Phase 7: v1 higher-tier components from Mine depths 4-6 and Forest areas 4-6 (GDD 6/7).
            new ResourceSpec { Key = "moonsilver_ore", Name = "Moonsilver Ore", Category = ResourceCategory.RawMetal, Receiver = ReceiverType.Smelter },
            new ResourceSpec { Key = "ironwood_log", Name = "Ironwood Log", Category = ResourceCategory.RawTimber, Receiver = ReceiverType.Lumbermill },
            new ResourceSpec { Key = "moonsilver_ingot", Name = "Moonsilver Ingot", Category = ResourceCategory.Processed, Receiver = ReceiverType.None },
            new ResourceSpec { Key = "ironwood_lumber", Name = "Ironwood Lumber", Category = ResourceCategory.Processed, Receiver = ReceiverType.None },
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
            // Phase 5: quest-unlocked advanced items (GDD 9). Trader values ~10-15 % of replacement, conservative.
            new ItemSpec { Key = "maple_longbow", Name = "Maple Longbow", Category = ItemCategory.Weapon, Power = 11, Tag = WeaponTag.Bow, BuyValue = 4 },
            new ItemSpec { Key = "steel_sword", Name = "Steel Sword", Category = ItemCategory.Weapon, Power = 13, Tag = WeaponTag.Blade, BuyValue = 5 },
            new ItemSpec { Key = "steel_coat", Name = "Steel Coat", Category = ItemCategory.Armor, Protection = 10, BuyValue = 5 },
            new ItemSpec { Key = "antidote", Name = "Antidote", Category = ItemCategory.Potion, Effect = PotionEffect.PoisonCounter, Strength = 6, BuyValue = 2 },
            new ItemSpec { Key = "warding_draught", Name = "Warding Draught", Category = ItemCategory.Potion, Effect = PotionEffect.ArcaneCounter, Strength = 6, BuyValue = 2 },
            // Phase 7 advanced catalogue (balance starting points; Trader values ~10-15 % of replacement).
            new ItemSpec { Key = "steel_maul", Name = "Steel Maul", Category = ItemCategory.Weapon, Power = 14, Tag = WeaponTag.Blunt, BuyValue = 6 },
            new ItemSpec { Key = "steel_stiletto", Name = "Steel Stiletto", Category = ItemCategory.Weapon, Power = 13, Tag = WeaponTag.LightBlade, BuyValue = 5 },
            new ItemSpec { Key = "fur_brigandine", Name = "Fur-lined Brigandine", Category = ItemCategory.Armor, Protection = 8, BuyValue = 4 },
            new ItemSpec { Key = "strong_tonic", Name = "Strong Tonic", Category = ItemCategory.Potion, Effect = PotionEffect.SurvivalMargin, Strength = 7, BuyValue = 3 },
            new ItemSpec { Key = "pathfinder_draught", Name = "Pathfinder's Draught", Category = ItemCategory.Potion, Effect = PotionEffect.ExplorationCounter, Strength = 6, BuyValue = 2 },
            new ItemSpec { Key = "trapsense_draught", Name = "Trapsense Draught", Category = ItemCategory.Potion, Effect = PotionEffect.TrapsCounter, Strength = 6, BuyValue = 2 },
            new ItemSpec { Key = "moonsilver_focus", Name = "Moonsilver Focus", Category = ItemCategory.Weapon, Power = 15, Tag = WeaponTag.Staff, BuyValue = 7 },
            new ItemSpec { Key = "moonsilver_mail", Name = "Moonsilver Mail", Category = ItemCategory.Armor, Protection = 14, BuyValue = 8 },
            new ItemSpec { Key = "ironwood_greatbow", Name = "Ironwood Greatbow", Category = ItemCategory.Weapon, Power = 16, Tag = WeaponTag.Bow, BuyValue = 7 },
            new ItemSpec { Key = "moonsilver_blade", Name = "Moonsilver Blade", Category = ItemCategory.Weapon, Power = 17, Tag = WeaponTag.Blade, BuyValue = 8 },
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
            foreach (var (key, tag, strength) in new[] { ("fur_brigandine", CounterTag.Exploration, 3), ("moonsilver_focus", CounterTag.Arcane, 5) })
            {
                var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDir + "item_" + key + ".asset");
                if (it != null && it.CounterTag == CounterTag.None) { it.EditorSetCounter(tag, strength); EditorUtility.SetDirty(it); }
            }
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

            SiteDefinition Site(string key, string name, SiteType type, List<SiteBandDefinition> bands, string staging,
                int units, float baseD, float growth, float round, float kl, float kq)
            {
                var path = SiteDir + "site_" + key + ".asset";
                var created = !File.Exists(path);
                var site = EnsureAsset<SiteDefinition>(path);
                if (!created) return site;
                site.EditorInitialize("site." + key, name);
                site.EditorSetupSite(type, bands, new List<WorkerDefinition>(), staging, units, 6, baseD, growth, round, kl, kq, 0.8f);
                EditorUtility.SetDirty(site);
                return site;
            }
            var mine = Site("mine", "Mine", SiteType.Mine, layers, "container.mine_staging", 24, 240f, 1.22f, 10f, 0.18f, 0.04f);
            var forest = Site("forest", "Forest", SiteType.Forest, zones, "container.forest_staging", 4, 40f, 1.18f, 5f, 0.15f, 0.03f);

            if (catalogue.Sites.Count == 0)
            {
                var bands = new List<SiteBandDefinition>(layers);
                bands.AddRange(zones);
                catalogue.EditorSetSites(new List<SiteDefinition> { mine, forest }, bands, new List<WorkerDefinition>(), equipment);
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
            void Recipe(string item, StationDefinition st, List<ResourceCost> cost, bool starter, string reveal, int level = 1)
            {
                var bp = Make<BlueprintDefinition>("blueprint_" + item, "blueprint." + item, I(item).DisplayName + " blueprint", out var bpCreated);
                if (bpCreated) { bp.EditorSetupBlueprint(starter, reveal); EditorUtility.SetDirty(bp); }
                var r = Make<RecipeDefinition>("recipe_" + item, "recipe." + item, I(item).DisplayName, out var rCreated);
                if (rCreated) { r.EditorSetupRecipe(I(item), cost, st, level, bp); EditorUtility.SetDirty(r); }
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
            // Phase 5 quest rewards (GDD 9 table; Forge 2 and Apothecary 2 gate advanced recipes, GDD 16).
            Recipe("maple_longbow", bench, Cost(("maple_lumber", 4), ("iron_ingot", 2)), false, "A longbow that hits harder from further away.");
            Recipe("steel_sword", forge, Cost(("steel_ingot", 4), ("cedar_lumber", 2)), false, "A heavier, keener blade.", 2);
            Recipe("steel_coat", forge, Cost(("steel_ingot", 5), ("maple_lumber", 2)), false, "Armor that turns heavy blows.", 2);
            Recipe("antidote", apothecary, Cost(("herb", 2), ("copper_ingot", 1)), false, "Counters poison when it is present.", 2);
            Recipe("warding_draught", apothecary, Cost(("herb", 2), ("arcane_dust", 1)), false, "Counters arcane hazards when present.", 2);

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
            else
            {
                var r = new List<RecipeDefinition>(catalogue.Recipes);
                var b = new List<BlueprintDefinition>(catalogue.Blueprints);
                var changed = false;
                foreach (var x in recipes) if (!r.Contains(x)) { r.Add(x); changed = true; }
                foreach (var x in blueprints) if (!b.Contains(x)) { b.Add(x); changed = true; }
                if (changed)
                {
                    catalogue.EditorSetProduction(new List<StationDefinition>(catalogue.Stations), new List<ProcessDefinition>(catalogue.Processes), r, b, catalogue.StartingState);
                    EditorUtility.SetDirty(catalogue);
                }
            }
        }

        public const string JobsDir = Root + "Content/Jobs/";

        /// <summary>Phase 4 content (GDD 10/11/17 balance starting points). Creates missing assets only.</summary>
        private static void EnsureJobContent(ContentCatalogue catalogue)
        {
            T Make<T>(string file, string id, string name, out bool created) where T : ContentDefinition
            {
                var path = JobsDir + file + ".asset";
                created = !File.Exists(path);
                var a = EnsureAsset<T>(path);
                if (created) a.EditorInitialize(id, name);
                return a;
            }
            List<CounterValue> C(params (CounterTag t, int s)[] c)
            {
                var l = new List<CounterValue>();
                foreach (var (t, st) in c) l.Add(new CounterValue { tag = t, strength = st });
                return l;
            }

            AdventurerClassDefinition Class(string key, string name, int m, int s, int i, WeaponTag[] tags, List<CounterValue> counters, int fee, bool rare, string overlay, string role)
            {
                var c = Make<AdventurerClassDefinition>("class_" + key, "class." + key, name, out var created);
                if (created) { c.EditorSetupClass(m, s, i, new List<WeaponTag>(tags), counters, fee, rare, LoadSprite("Characters/" + overlay), role); EditorUtility.SetDirty(c); }
                return c;
            }
            var warrior = Class("warrior", "Warrior", 8, 8, 3, new[] { WeaponTag.Blade, WeaponTag.Blunt }, C(), 8, false, "ov_warrior", "Reliable combat and survival.");
            var ranger = Class("ranger", "Ranger", 5, 7, 8, new[] { WeaponTag.Bow }, C((CounterTag.Exploration, 2)), 9, false, "ov_ranger", "Exploration: +2 exploration counter.");
            var rogue = Class("rogue", "Rogue", 7, 4, 8, new[] { WeaponTag.LightBlade }, C((CounterTag.Traps, 2)), 10, true, "ov_rogue", "+2 trap counter and a bonus rare reward roll on success; fragile.");
            var mage = Class("mage", "Mage", 9, 4, 7, new[] { WeaponTag.Staff }, C((CounterTag.Arcane, 4)), 13, false, "ov_mage", "+4 arcane counter; expensive.");

            TraitDefinition Trait(string key, string name, WillingnessRule w, List<CounterValue> counters, TraitScope scope, string desc)
            {
                var t = Make<TraitDefinition>("trait_" + key, "trait." + key, name, out var created);
                if (created) { t.EditorSetupTrait(w, counters, scope, desc); EditorUtility.SetDirty(t); }
                return t;
            }
            var cautious = Trait("cautious", "Cautious", WillingnessRule.Cautious, C(), TraitScope.Application, "Applies only when the job looks Good or better.");
            var bold = Trait("bold", "Bold", WillingnessRule.Bold, C(), TraitScope.Application, "Will take jobs that look Dangerous.");
            var woodland = Trait("woodland_familiar", "Woodland Familiar", WillingnessRule.Neutral, C((CounterTag.Exploration, 2)), TraitScope.Resolution, "+2 exploration counter.");
            var poison = Trait("poison_trained", "Poison Trained", WillingnessRule.Neutral, C((CounterTag.Poison, 2)), TraitScope.Resolution, "+2 poison counter (helps, but needs 4 to counter poison).");

            ThreatDefinition Threat(string key, string name, CounterTag tag, int req, int unmetSurv, int armor, string reveal)
            {
                var t = Make<ThreatDefinition>("threat_" + key, "threat." + key, name, out var created);
                if (created) { t.EditorSetupThreat(tag, req, -4, unmetSurv, armor, armor > 0 ? 2 : 0, armor > 0 ? 2 : 0, reveal); EditorUtility.SetDirty(t); }
                return t;
            }
            var exploration = Threat("exploration", "Hard to find the way", CounterTag.Exploration, 2, 0, 0, "Rangers or a known route help.");
            var arcane = Threat("arcane", "Arcane hazard", CounterTag.Arcane, 4, 0, 0, "Mages or ward gear counter it.");
            var heavy = Threat("heavy_blows", "Heavy blows", CounterTag.HeavyBlows, 0, 0, 8, "Armor with Protection 8 or more holds.");
            var poisonT = Threat("poison", "Poison", CounterTag.Poison, 4, -3, 0, "An antidote counters it; uncountered it also threatens survival.");
            var traps = Threat("traps", "Traps", CounterTag.Traps, 2, 0, 0, "Rogues spot and disarm traps.");

            QuestDefinition Quest(string key, string name, int d, float seconds, int gold, bool recovery, int prestige, string brief, params (ThreatDefinition t, bool known)[] threats)
            {
                var q = Make<QuestDefinition>("quest_" + key, "quest." + key, name, out var created);
                if (!created) return q;
                var list = new List<QuestThreat>();
                foreach (var (t, known) in threats) list.Add(new QuestThreat { threat = t, knownAtStart = known });
                q.EditorSetupQuest(d, seconds, list, gold, true, recovery, prestige, brief);
                EditorUtility.SetDirty(q);
                return q;
            }
            var quests = new List<QuestDefinition>
            {
                Quest("local_supply_survey", "Local Supply Survey", 0, 60f, 8, true, 20, "A patron-sponsored survey of the nearby roads. Always safe; pays little."),
                Quest("roadside_patrol", "Roadside Patrol", 14, 90f, 28, false, 20, "Walk the trade road and see off anything bothering travellers."),
                Quest("briar_track", "Briar Track", 18, 120f, 36, false, 25, "An overgrown trail into the hills.", (exploration, true)),
                Quest("old_shrine", "Old Shrine", 20, 150f, 44, false, 30, "A ruined shrine where strange lights are reported.", (arcane, true)),
                Quest("quarry_road", "Quarry Road", 22, 180f, 48, false, 35, "The abandoned quarry road.", (heavy, false)),
                Quest("fen_path", "Fen Path", 23, 180f, 50, false, 35, "A path through the fens; travellers come back sick.", (poisonT, true)),
                Quest("fallen_watchtower", "Fallen Watchtower", 24, 210f, 54, false, 40, "A collapsed watchtower with something still inside.", (traps, false)),
                Quest("shrine_chamber", "Shrine Chamber", 27, 240f, 65, false, 45, "The sealed chamber beneath the Old Shrine.", (arcane, true), (exploration, false)),
            };

            // Phase 5 progression: GDD 17 rewards and the map chain (balance starting points).
            QuestDefinition Q(string key) => quests.Find(x => x.Id == "quest." + key);
            ResourceDefinition Res(string key) => catalogue.Get<ResourceDefinition>("resource." + key);
            List<ResourceCost> M(params (string r, int q)[] c)
            {
                var l = new List<ResourceCost>();
                foreach (var (r, qty) in c) l.Add(new ResourceCost { resource = Res(r), quantity = qty });
                return l;
            }
            BlueprintDefinition B(string key) => AssetDatabase.LoadAssetAtPath<BlueprintDefinition>(ProductionDir + "blueprint_" + key + ".asset");
            void Progression(string key, string[] prereqs, int gate, int tier, List<ResourceCost> success, List<ResourceCost> great, string blueprint, List<ResourceCost> sub, string rare, float chance)
            {
                var q = Q(key);
                var configured = q.SuccessMaterials.Count > 0 || q.FirstSuccessBlueprint != null || q.Prerequisites.Count > 0 || q.MapTier > 0;
                if (configured || q.RecoveryContract) return;
                var pre = new List<QuestDefinition>();
                foreach (var p in prereqs) pre.Add(Q(p));
                q.EditorSetupProgression(pre, gate, tier, success, great, new List<ResourceCost>(), blueprint != null ? B(blueprint) : null, sub, rare != null ? Res(rare) : null, chance);
                EditorUtility.SetDirty(q);
            }
            Progression("roadside_patrol", new string[0], 0, 0, M(("iron_ingot", 2)), M(("iron_ingot", 1)), null, M(), "herb", 0.25f);
            Progression("briar_track", new[] { "roadside_patrol" }, 0, 1, M(("maple_lumber", 2)), M(("maple_lumber", 1)), "maple_longbow", M(("maple_lumber", 2)), "gem", 0.5f);
            Progression("old_shrine", new[] { "roadside_patrol" }, 0, 1, M(("arcane_dust", 1)), M(("arcane_dust", 1)), "copper_ward_staff", M(("copper_ingot", 2)), "gem", 0.5f);
            Progression("quarry_road", new[] { "briar_track" }, 0, 2, M(("iron_ingot", 3)), M(("iron_ingot", 1)), "steel_coat", M(("iron_ingot", 2)), "gem", 0.5f);
            Progression("fen_path", new[] { "briar_track" }, 0, 2, M(("herb", 2)), M(("herb", 1)), "antidote", M(("herb", 2)), "gem", 0.5f);
            Progression("fallen_watchtower", new[] { "old_shrine" }, 0, 2, M(("steel_ingot", 2)), M(("steel_ingot", 1)), "steel_sword", M(("iron_ingot", 2)), "gem", 0.5f);
            Progression("shrine_chamber", new[] { "old_shrine" }, 30, 3, M(("arcane_dust", 2), ("copper_ingot", 2)), M(("arcane_dust", 1)), "warding_draught", M(("copper_ingot", 2)), "gem", 0.5f);

            ContractorDefinition Contractor(string key, string name, AdventurerClassDefinition cls, TraitDefinition trait, bool sponsored, int skin)
            {
                var c = Make<ContractorDefinition>("contractor_" + key, "contractor." + key, name, out var created);
                if (created)
                {
                    c.EditorSetupContractor(cls, 1, trait != null ? new List<TraitDefinition> { trait } : new List<TraitDefinition>(), 1f, sponsored, skin);
                    EditorUtility.SetDirty(c);
                }
                return c;
            }
            // GDD 11 example fixtures (neutral personality price 1.0 so the GDD's example quotes hold).
            var contractors = new List<ContractorDefinition>
            {
                Contractor("elin_moss", "Elin Moss", ranger, woodland, false, 0),
                Contractor("bram_flint", "Bram Flint", warrior, cautious, false, 1),
                Contractor("nessa_quick", "Nessa Quick", rogue, bold, false, 2),
                Contractor("ivo_reed", "Ivo Reed", mage, poison, false, 1),
                Contractor("wren_tallow", "Wren Tallow (sponsored)", warrior, null, true, 0),
            };

            var poolPath = ConfigDir + "ContractorNamePool.asset";
            var poolCreated = !File.Exists(poolPath);
            var pool = EnsureAsset<ContractorNamePool>(poolPath);
            if (poolCreated)
            {
                pool.EditorSetup(
                    new List<string> { "Aldis", "Bryn", "Cato", "Delia", "Edda", "Fenn", "Garrow", "Hale", "Isolde", "Joss", "Kestra", "Lark", "Maren", "Nils", "Orla", "Pim", "Rook", "Sable", "Tamsin", "Ulric" },
                    new List<string> { "Ashdown", "Brackwater", "Cinderfell", "Dunmore", "Elmsworth", "Fallow", "Greythorn", "Holloway", "Ironside", "Juniper", "Kettleby", "Larkmoor", "Mossgrave", "Northcote", "Oakhurst", "Pellow", "Quarrie", "Rushford", "Stonebridge", "Thistlewood" });
                EditorUtility.SetDirty(pool);
            }

            if (catalogue.Classes.Count == 0)
            {
                catalogue.EditorSetJobs(new List<AdventurerClassDefinition> { warrior, ranger, rogue, mage },
                    new List<TraitDefinition> { cautious, bold, woodland, poison },
                    new List<ThreatDefinition> { exploration, arcane, heavy, poisonT, traps }, quests, contractors, pool);
                EditorUtility.SetDirty(catalogue);
            }
        }

        public const string EconomyDir = Root + "Content/Economy/";

        /// <summary>Phase 6: the Trader's stock, GDD 16 upgrades and crew recruits (balance starting points).</summary>
        private static void EnsureEconomyContent(ContentCatalogue catalogue)
        {
            ResourceDefinition R(string key) => catalogue.Get<ResourceDefinition>("resource." + key);
            List<ResourceCost> M(params (string r, int q)[] c)
            {
                var l = new List<ResourceCost>();
                foreach (var (r, qty) in c) l.Add(new ResourceCost { resource = R(r), quantity = qty });
                return l;
            }
            var mine = catalogue.Get<SiteDefinition>("site.mine");
            var forest = catalogue.Get<SiteDefinition>("site.forest");
            StationDefinition St(string key) => catalogue.Get<StationDefinition>("station." + key);
            QuestDefinition Q(string key) => catalogue.Get<QuestDefinition>("quest." + key);

            UpgradeDefinition Up(string key, string name, UpgradeKind kind, StationDefinition st, SiteDefinition si, int level, int gold,
                List<ResourceCost> mats, string effect, UpgradeDefinition reqUp = null, SiteDefinition reqSite = null, int reqBand = 0,
                QuestDefinition reqQuest = null, int reqSuccesses = 0)
            {
                var path = EconomyDir + "upgrade_" + key + ".asset";
                var created = !File.Exists(path);
                var u = EnsureAsset<UpgradeDefinition>(path);
                if (!created) return u;
                u.EditorInitialize("upgrade." + key, name);
                u.EditorSetupUpgrade(kind, st, si, level, gold, mats, effect, reqUp, reqSite, reqBand, reqQuest, reqSuccesses);
                EditorUtility.SetDirty(u);
                return u;
            }
            var smelter2 = Up("smelter_2", "Smelter 2", UpgradeKind.StationLevel, St("smelter"), null, 2, 50, M(("cedar_lumber", 8), ("iron_ingot", 6)),
                "Unlocks steel; basic batch time ×0.85", reqSite: mine, reqBand: 3);
            var mill2 = Up("lumbermill_2", "Lumbermill 2", UpgradeKind.StationLevel, St("lumbermill"), null, 2, 40, M(("iron_ingot", 6), ("cedar_lumber", 8)),
                "Unlocks maple sawing; basic batch time ×0.85", reqSite: forest, reqBand: 2);
            var forge2 = Up("forge_2", "Forge 2", UpgradeKind.StationLevel, St("forge"), null, 2, 70, M(("iron_ingot", 10), ("maple_lumber", 8)),
                "Enables advanced metal recipes", reqUp: smelter2);
            var apo2 = Up("apothecary_2", "Apothecary 2", UpgradeKind.StationLevel, St("apothecary"), null, 2, 45, M(("cedar_lumber", 6), ("copper_ingot", 4)),
                "Enables advanced potions", reqQuest: Q("old_shrine"));
            var mineEq2 = Up("mine_equipment_2", "Mine Equipment 2", UpgradeKind.SiteEquipment, null, mine, 2, 45, M(("iron_ingot", 6), ("cedar_lumber", 6)),
                "Unlocks iron tools and framed packs for purchase", reqSuccesses: 2);
            var forestEq2 = Up("forest_equipment_2", "Forest Equipment 2", UpgradeKind.SiteEquipment, null, forest, 2, 40, M(("iron_ingot", 6), ("cedar_lumber", 6)),
                "Unlocks iron axes and framed packs for purchase", reqSuccesses: 2);
            // Level 3 is not in the GDD table: an invented balance starting point for the tier 3 tools and packs.
            var mineEq3 = Up("mine_equipment_3", "Mine Equipment 3", UpgradeKind.SiteEquipment, null, mine, 3, 90, M(("steel_ingot", 8), ("maple_lumber", 6)),
                "Unlocks tempered tools and reinforced packs for purchase", reqUp: mineEq2, reqSuccesses: 5);
            var forestEq3 = Up("forest_equipment_3", "Forest Equipment 3", UpgradeKind.SiteEquipment, null, forest, 3, 90, M(("steel_ingot", 8), ("maple_lumber", 6)),
                "Unlocks tempered axes and reinforced packs for purchase", reqUp: forestEq2, reqSuccesses: 5);

            var traderPath = EconomyDir + "trader_stock.asset";
            var traderCreated = !File.Exists(traderPath);
            var trader = EnsureAsset<TraderStockDefinition>(traderPath);
            if (traderCreated)
            {
                trader.EditorInitialize("trader.wandering", "Wandering Trader");
                TraderStockEntry E(string key, string res, int price, int qty, StockAvailability a, float chance = 1f, QuestDefinition after = null) =>
                    new TraderStockEntry { key = key, resource = res != null ? R(res) : null, sideBlueprint = res == null, price = price, quantity = qty, availability = a, chance = chance, afterQuest = after };
                trader.EditorSetupTrader(new List<TraderStockEntry>
                {
                    E("rope", "rope", 8, 3, StockAvailability.Chance, 0.7f),
                    E("fur", "fur", 8, 3, StockAvailability.Chance, 0.7f),
                    E("arcane_dust", "arcane_dust", 12, 2, StockAvailability.AfterQuest, 1f, Q("old_shrine")),
                    E("iron_bundle", "iron_ingot", 3, 6, StockAvailability.Guaranteed),
                    E("cedar_bundle", "cedar_lumber", 3, 6, StockAvailability.Guaranteed),
                    E("herbs", "herb", 4, 4, StockAvailability.Chance, 0.7f),
                    E("side_blueprint", null, 45, 1, StockAvailability.Chance, 0.2f),
                }, new List<BlueprintDefinition>(), 20f, 5f, 60f);
                EditorUtility.SetDirty(trader);
            }

            if (catalogue.TraderStock == null || catalogue.Upgrades.Count == 0)
            {
                catalogue.EditorSetEconomy(trader, new List<UpgradeDefinition> { smelter2, mill2, forge2, apo2, mineEq2, forestEq2, mineEq3, forestEq3 });
                EditorUtility.SetDirty(catalogue);
            }
            AssetDatabase.SaveAssets();
        }

        public const string SupplierAnimationsPath = ConfigDir + "SupplierAnimations.asset";

        /// <summary>
        /// The supplier's animation clips, rebuilt from Characters/Supplier/animations.json (written by
        /// Tools/ArtImport/import_character_strips.py): generated data, so it is replaced, not merged.
        /// </summary>
        public const string StoreOwnerPath = ConfigDir + "StoreOwner.asset";
        private const string UnlitSpriteMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";

        /// <summary>
        /// The rubber-hose supplier (Astra's General Store Owner, Phase 2; 2026-10-14): his parts and anchor points, from
        /// Characters/StoreOwner/points.json. Positions become units from the ground point (256, 444), y up, at the
        /// parts' shared 340 px per unit. Motion tunables on the asset are kept when edited.
        /// </summary>
        public static HoseCharacterDefinition EnsureStoreOwner()
        {
            const string dir = "Characters/StoreOwner/";
            var json = PlaceholderSpriteImporter.ArtRoot + dir + "points.json";
            if (!File.Exists(json)) return null;
            const float ppu = 340f;
            var p = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(json));
            Vector2 U(Newtonsoft.Json.Linq.JToken xy) => new Vector2(((float)xy[0] - 256f) / ppu, (444f - (float)xy[1]) / ppu);
            var def = EnsureAsset<HoseCharacterDefinition>(StoreOwnerPath);
            var views = new List<HoseCharacterDefinition.View>();
            foreach (var name in new[] { "S", "SE", "E", "NE", "N" })
            {
                var b = p["body_" + name];
                var wrists = p["_meta"]["preview"]["wrist_targets"][name];
                views.Add(new HoseCharacterDefinition.View
                {
                    name = name,
                    body = LoadSprite(dir + "body_" + name),
                    bootLeft = LoadSprite(dir + "boot_" + name + "_left"),
                    bootRight = LoadSprite(dir + "boot_" + name + "_right"),
                    shoulderLeft = U(b["shoulder_left"]), shoulderRight = U(b["shoulder_right"]),
                    hipLeft = U(b["hip_left"]), hipRight = U(b["hip_right"]),
                    ankleLeft = U(p["boot_" + name + "_left"]["ankle"]), ankleRight = U(p["boot_" + name + "_right"]["ankle"]),
                    wristLeft = U(wrists["left"]), wristRight = U(wrists["right"]),
                });
            }
            // The side view's arms (Kevin, 2026-10-14: "fix the arm placement from the side"): the anchors hung the near arm
            // behind his back. The near (right) arm comes out of the sleeve's opening and hangs straight down, a touch
            // forward; the far arm hangs just behind the torso's front, hidden by it.
            var side = views.Find(v => v.name == "E");
            if (side != null)
            {
                side.shoulderRight = U(new Newtonsoft.Json.Linq.JArray(222, 284));
                side.wristRight = U(new Newtonsoft.Json.Linq.JArray(230, 352));
                side.shoulderLeft = U(new Newtonsoft.Json.Linq.JArray(262, 272));
                side.wristLeft = U(new Newtonsoft.Json.Linq.JArray(268, 345));
            }
            def.views = views.ToArray();
            def.handOpenFront = LoadSprite(dir + "hand_open_front"); def.handOpenSide = LoadSprite(dir + "hand_open_side"); def.handOpenBack = LoadSprite(dir + "hand_open_back");
            def.handGripFront = LoadSprite(dir + "hand_grip_front"); def.handGripSide = LoadSprite(dir + "hand_grip_side"); def.handGripBack = LoadSprite(dir + "hand_grip_back");
            def.handPointFront = LoadSprite(dir + "hand_point_front"); def.handPointSide = LoadSprite(dir + "hand_point_side"); def.handPointBack = LoadSprite(dir + "hand_point_back");
            def.pickaxe = LoadSprite(dir + "pickaxe");
            def.axe = LoadSprite(dir + "axe");
            var limbs = p["limbs"];
            def.armColor = ColorUtility.TryParseHtmlString((string)limbs["arm_colour"], out var ac) ? ac : def.armColor;
            def.legColor = ColorUtility.TryParseHtmlString((string)limbs["leg_colour"], out var lc) ? lc : def.legColor;
            // Hoses a little wider than the art's straight preview tubes, tapering to the wrist and ankle (Kevin,
            // 2026-10-14). Set once; edits on the asset are kept.
            if (def.armWidthEnd <= 0f || def.armWidth <= 0.0531f && def.armWidth >= 0.0529f)
            {
                def.armWidth = (float)limbs["arm_width_px"] * 1.45f / ppu;
                def.armWidthEnd = (float)limbs["arm_width_px"] * 0.85f / ppu;
                def.legWidth = (float)limbs["leg_width_px"] * 1.5f / ppu;
                def.legWidthEnd = (float)limbs["leg_width_px"] * 0.85f / ppu;
            }
            def.limbMaterial = AssetDatabase.LoadAssetAtPath<Material>(UnlitSpriteMaterialPath);
            EditorUtility.SetDirty(def);
            return def;
        }

        public const string OutlineSettingsPath = ConfigDir + "OutlineSettings.asset";
        public const string OutlineMaterialPath = ConfigDir + "OutlineSilhouette.mat";

        /// <summary>
        /// The game-drawn outline (2026-10-14): its material, its settings (thickness and colour are kept when edited),
        /// and the list of outlined sprites, rebuilt from "outline": true in sprite_meta.json, the per-asset switch.
        /// </summary>
        public static OutlineSettings EnsureOutlines(SandboxSettings sandbox)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (mat == null)
            {
                var shader = Shader.Find("Ravenholm/SpriteOutlineSilhouette");
                if (shader == null) { Debug.LogError("[Setup] Outline shader missing."); return null; }
                mat = new Material(shader) { name = "OutlineSilhouette" };
                AssetDatabase.CreateAsset(mat, OutlineMaterialPath);
            }
            var outlines = EnsureAsset<OutlineSettings>(OutlineSettingsPath);
            outlines.material = mat;
            outlines.outlinedSprites.Clear();
            var meta = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(PlaceholderSpriteImporter.ArtRoot + "sprite_meta.json"));
            foreach (var p in (Newtonsoft.Json.Linq.JObject)meta["sprites"])
            {
                var flag = p.Value["outline"];
                if (flag == null || !(bool)flag) continue;
                var rel = p.Key;
                var sprite = LoadSprite(rel.Substring(0, rel.Length - 4));
                if (sprite != null) outlines.outlinedSprites.Add(sprite);
                else Debug.LogWarning($"[Setup] Outlined sprite not found: {rel}");
            }
            EditorUtility.SetDirty(outlines);
            if (sandbox != null && sandbox.outlines != outlines)
            {
                sandbox.outlines = outlines;
                EditorUtility.SetDirty(sandbox);
            }
            Debug.Log($"[Setup] Outlines: {outlines.outlinedSprites.Count} sprites use the game's outline.");
            return outlines;
        }

        /// <summary>
        /// Each carried piece's pile spacing, from the stack art's spacing file (Stacks/stack_spacing.json: Astra's
        /// measured values, 2026-10-14). The file is the source: rerunning setup reapplies it.
        /// </summary>
        public static void EnsureStackSteps()
        {
            var json = PlaceholderSpriteImporter.ArtRoot + "Stacks/stack_spacing.json";
            if (!File.Exists(json)) return;
            var steps = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(json))["perItem"];
            foreach (var p in steps)
            {
                var step = (float)p.Value;
                var res = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(ResourceDir + "resource_" + p.Key + ".asset");
                if (res != null) { res.EditorSetStackStep(step); EditorUtility.SetDirty(res); continue; }
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDir + "item_" + p.Key + ".asset");
                if (item != null) { item.EditorSetStackStep(step); EditorUtility.SetDirty(item); continue; }
                Debug.LogWarning($"[Setup] Stack spacing for unknown id {p.Key}");
            }
        }

        public static CharacterAnimationSet EnsureSupplierAnimations()
        {
            var set = EnsureAsset<CharacterAnimationSet>(SupplierAnimationsPath);
            var json = PlaceholderSpriteImporter.ArtRoot + "Characters/Supplier/animations.json";
            if (!File.Exists(json)) return set;
            var root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(json));
            set.clips.Clear();
            foreach (var p in (Newtonsoft.Json.Linq.JObject)root["clips"])
            {
                var clip = new CharacterAnimationSet.Clip { name = p.Key, fps = (float)p.Value["fps"], loop = (bool)p.Value["loop"] };
                var frames = (Newtonsoft.Json.Linq.JArray)p.Value["frames"];
                clip.frames = new Sprite[frames.Count];
                var carry = new List<Vector2>();
                var durations = new List<float>();
                var anyCarry = false;
                for (var i = 0; i < frames.Count; i++)
                {
                    var rel = (string)frames[i]["sprite"];
                    clip.frames[i] = LoadSprite(rel.Substring(0, rel.Length - 4));
                    var d = frames[i]["dur"];
                    durations.Add(d != null ? (float)d : 1f / Mathf.Max(0.01f, clip.fps));
                    var c = frames[i]["carry"];
                    anyCarry |= c != null;
                    carry.Add(c != null ? new Vector2((float)c[0], (float)c[1]) : Vector2.zero);
                }
                // Only frames up to the last one with a carry point carry one (pick-up gets it on its last frame).
                clip.carry = anyCarry ? carry.ToArray() : new Vector2[0];
                clip.durations = durations.ToArray();
                var v = p.Value["velocity"];
                clip.velocity = v != null ? new Vector2((float)v[0], (float)v[1]) : Vector2.zero;
                set.clips.Add(clip);
            }
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return set;
        }

        /// <summary>The Mine's scaffolds are built from lumber (any kind) from the town's stock; edited values are kept.</summary>
        private static void EnsureScaffoldMaterials()
        {
            var mine = AssetDatabase.LoadAssetAtPath<SiteDefinition>(SiteDir + "site_mine.asset");
            if (mine == null) { Debug.LogWarning("[Setup] Mine site definition not found for scaffold materials."); return; }
            var so = new SerializedObject(mine);
            var list = so.FindProperty("scaffoldMaterials");
            if (list.arraySize > 0) return;
            foreach (var id in new[] { "cedar_lumber", "maple_lumber", "ironwood_lumber" })
            {
                var res = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(ResourceDir + "resource_" + id + ".asset");
                if (res == null) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = res;
            }
            so.FindProperty("scaffoldCost").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The painted ore blocks (Art/Placeholder/Painted/Blocks) on the Mine's ores; edited values are kept.</summary>
        private static void EnsureOreBlocks()
        {
            foreach (var (id, sprite) in new[] { ("iron_ore", "ore_iron"), ("copper_ore", "ore_copper"), ("coal", "ore_coal"), ("moonsilver_ore", "ore_moonsilver") })
            {
                var res = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(ResourceDir + "resource_" + id + ".asset");
                if (res == null) continue;
                var so = new SerializedObject(res);
                var p = so.FindProperty("mineBlock");
                if (p.objectReferenceValue != null) continue;
                p.objectReferenceValue = LoadSprite("Painted/Blocks/block_" + sprite);
                so.ApplyModifiedPropertiesWithoutUndo();
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
            if (t.board == null) { t.board = LoadSprite("UI/Icons/ic_board"); EditorUtility.SetDirty(t); }
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
            foreach (var key in new[] { "grass_a", "grass_b", "grass_c", "path", "path_b", "path_c", "plaza", "dirt", "forest_floor", "forest_floor_b", "block_0", "block_1", "block_2", "block_seam_0", "block_seam_1", "block_seam_2", "slab", "sill", "fleck", "halfblock_0", "halfblock_1", "halfblock_2", "fleck_half", "halfblock_outline_0", "halfblock_outline_1", "halfblock_outline_2" })
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
