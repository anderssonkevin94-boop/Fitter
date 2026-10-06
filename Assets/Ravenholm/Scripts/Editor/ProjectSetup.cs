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
            if (!EnsureTmpEssentials())
            {
                Debug.LogError("[Setup] TextMesh Pro essentials are missing. Batch mode cannot import packages from a method; run Unity once with " +
                               "-importPackage \"<project>/Library/PackageCache/com.unity.ugui@*/Package Resources/TMP Essential Resources.unitypackage\" " +
                               "(Tools/setup.sh does this), or use Window > TextMeshPro > Import TMP Essential Resources, then rerun setup.");
                return false;
            }
            ReimportArt();

            var catalogue = EnsureContent();
            var settings = EnsureAsset<SandboxSettings>(ConfigDir + "SandboxSettings.asset");
            var theme = EnsureUiTheme();
            var boot = EnsureAsset<BootConfig>(ConfigDir + "BootConfig.asset");
            if (boot.Catalogue == null || boot.SandboxSettings == null || boot.UiTheme == null)
            {
                boot.EditorSetup(boot.Catalogue != null ? boot.Catalogue : catalogue,
                    boot.SandboxSettings != null ? boot.SandboxSettings : settings,
                    boot.UiTheme != null ? boot.UiTheme : theme);
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
            return catalogue;
        }

        private static UiTheme EnsureUiTheme()
        {
            var path = ConfigDir + "UiTheme.asset";
            var created = !File.Exists(path);
            var t = EnsureAsset<UiTheme>(path);
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
