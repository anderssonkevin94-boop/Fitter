using System.Collections.Generic;
using Ravenholm.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Ravenholm.EditorTools
{
    /// <summary>
    /// Generates the Bootstrap and DebugSandbox scenes from the placeholder kit. The output
    /// is an ordinary scene you can then edit by hand (and should, once layout is approved).
    /// Layout follows GDD 3: Store central, Mine and metal facilities on one side, Forest and
    /// lumbermill on the other, apothecary and Trader pad near the Store.
    /// </summary>
    public static class SandboxSceneBuilder
    {
        private const string UnlitMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";
        private static readonly Color Backdrop = new Color32(58, 86, 92, 255);

        private static Material _unlit;

        public static void BuildBootstrap(string bootConfigPath, string path)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Load after NewScene: creating a scene unloads unreferenced assets, leaving earlier references stale.
            var boot = AssetDatabase.LoadAssetAtPath<BootConfig>(bootConfigPath);
            if (boot == null) throw new System.IO.FileNotFoundException("BootConfig not found", bootConfigPath);

            var rootGo = new GameObject("GameRoot");
            var root = rootGo.AddComponent<GameRoot>();
            var so = new SerializedObject(root);
            so.FindProperty("config").objectReferenceValue = boot;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Bootstrap is on screen for a frame; give it a camera so it is not a "no camera" flash.
            var cam = new GameObject("BootCamera").AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Backdrop;

            EditorSceneManager.SaveScene(scene, path);
        }

        public static void BuildSandbox(string settingsPath, string path)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Load after NewScene (see BuildBootstrap).
            _unlit = AssetDatabase.LoadAssetAtPath<Material>(UnlitMaterialPath);
            var settings = AssetDatabase.LoadAssetAtPath<SandboxSettings>(settingsPath);
            if (settings == null) throw new System.IO.FileNotFoundException("SandboxSettings not found", settingsPath);
            var tiles = ProjectSetup.EnsureTiles();

            new GameObject("SceneBootGuard").AddComponent<SceneBootGuard>();

            BuildGround(settings, tiles);

            var world = new GameObject("World").transform;
            var buildings = new GameObject("Buildings").transform; buildings.SetParent(world);
            var nature = new GameObject("Nature").transform; nature.SetParent(world);
            var props = new GameObject("Props").transform; props.SetParent(world);
            var people = new GameObject("People").transform; people.SetParent(world);

            // --- buildings (positions are footprint centres in world units) ---
            Building(buildings, "Store", "bld_store", new Vector2(0f, 3.2f), new Vector2(3.1f, 2.6f),
                "Home base: inventory and display racks, Job Board, applicant meeting point and loadout hub. Open front; it is not a customer shop.",
                "Phase 3 stocking and racks · Phase 4 Job Board and applicants");
            Building(buildings, "Smelter", "bld_smelter", new Vector2(-7.2f, 0.6f), new Vector2(2.2f, 2.4f),
                "Receives iron ore, copper ore, coal and gems, then turns ore into ingots on its own.", "Phase 3 timed processing");
            Building(buildings, "Forge", "bld_forge", new Vector2(-5.4f, -3.8f), new Vector2(2.2f, 2.0f),
                "Covered work area. Crafts metal weapons and armor instantly from permanent blueprints.", "Phase 3 crafting");
            Building(buildings, "Lumbermill", "bld_lumbermill", new Vector2(7.2f, 0.6f), new Vector2(2.4f, 2.0f),
                "Receives logs and saws them into lumber on its own.", "Phase 3 timed processing");
            Building(buildings, "Workbench", "bld_workbench", new Vector2(5.6f, -3.8f), new Vector2(1.7f, 1.8f),
                "Crafts bows and staves instantly.", "Phase 3 crafting");
            Building(buildings, "Apothecary", "bld_apothecary", new Vector2(-2.9f, -1.7f), new Vector2(2.2f, 2.2f),
                "Receives herbs. Potions are crafted here.", "Phase 3 crafting");
            Building(buildings, "Trader Pad", "bld_trader_pad", new Vector2(3.0f, -2.1f), new Vector2(2.2f, 2.0f),
                "A periodic visitor sets up here to sell external materials and buy equipment at poor prices.", "Phase 6 Trader");
            Building(buildings, "Mine", "bld_mine_mouth", new Vector2(-9.0f, 6.0f), new Vector2(4.2f, 2.8f),
                "One active production site. Workers strike the current face; you choose to continue this layer or reveal a deeper one.",
                "Phase 2 workers and depth decisions");
            Building(buildings, "Forest", "bld_forest_gate", new Vector2(9.0f, 6.0f), new Vector2(2.2f, 1.9f),
                "Trail into the Forest. Workers chop the current clearing; Push Deeper reveals a new area further out.",
                "Phase 2 workers and area decisions");

            // --- nature ---
            var treeSpots = new[]
            {
                new Vector2(7.0f, 7.0f), new Vector2(10.6f, 5.2f), new Vector2(8.2f, 8.1f), new Vector2(11.0f, 7.6f),
                new Vector2(6.2f, 4.9f), new Vector2(11.3f, 3.6f), new Vector2(9.6f, 8.6f), new Vector2(5.4f, 7.6f),
            };
            for (var i = 0; i < treeSpots.Length; i++)
            {
                var cedar = i % 2 == 0;
                Prop(nature, cedar ? "Cedar Tree" : "Maple Tree", cedar ? "Nature/tree_cedar" : "Nature/tree_maple", treeSpots[i],
                    cedar ? "Placeholder cedar. Forest workers will claim individual trees." : "Placeholder maple. Deeper areas mix in more maple.",
                    "Phase 2");
            }
            Prop(nature, "Herb Bush", "Nature/bush_herb", new Vector2(8.6f, 3.3f), "Herbs come as a bonus while chopping.", "Phase 2");
            Prop(nature, "Herb Bush", "Nature/bush_herb", new Vector2(10.2f, 2.5f), "Herbs come as a bonus while chopping.", "Phase 2");
            Prop(nature, "Stone Face", "Nature/rock_stone", new Vector2(-11.0f, 3.5f), "Placeholder rock face (plain stone).", "Phase 2");
            Prop(nature, "Iron Face", "Nature/rock_iron", new Vector2(-6.9f, 4.0f), "Placeholder rock face with iron flecks.", "Phase 2");
            Prop(nature, "Copper Face", "Nature/rock_copper", new Vector2(-10.2f, 2.3f), "Placeholder rock face with copper flecks.", "Phase 2");

            // --- props: staging piles, outputs, store racks, bell ---
            Prop(props, "Mine Staging", "Props/pile_ore", new Vector2(-8.4f, 3.7f), "Workers drop ore here. You carry it to the Smelter.", "Phase 2");
            Prop(props, "Forest Staging", "Props/pile_logs", new Vector2(8.6f, 4.4f), "Workers drop logs here. You carry them to the Lumbermill.", "Phase 2");
            Prop(props, "Ingot Output", "Props/pile_ingots", new Vector2(-5.6f, -0.6f), "Representative processed ingots.", "Phase 3");
            Prop(props, "Lumber Output", "Props/pile_lumber", new Vector2(5.6f, -0.6f), "Representative processed lumber.", "Phase 3");
            Prop(props, "Weapon Rack", "Props/rack_weapons", new Vector2(-0.75f, 3.75f), "Shows a representative subset of stored weapons.", "Phase 3", 0.7f);
            Prop(props, "Armor Stand", "Props/rack_armor", new Vector2(0.0f, 4.15f), "Shows stored armor.", "Phase 3", 0.7f);
            Prop(props, "Potion Shelf", "Props/rack_potions", new Vector2(0.75f, 3.75f), "Shows stored potions.", "Phase 3", 0.7f);
            Bell(props, new Vector2(0.55f, 2.95f), 0.42f);
            Flowers(props, settings);

            // --- people ---
            var player = Character(people, "Supplier (you)", new Vector2(0f, 0f), 0, "ov_supplier",
                "You run the supply company. You walk, carry and craft; you never fight.",
                "Phase 1 movement, carrying and receivers", isPlayer: true);
            Character(people, "Bram Flint · Warrior", new Vector2(-1.9f, 1.75f), 1, "ov_warrior",
                "Example contractor fixture (level 1, Cautious). Independent; quotes their own fee.", "Phase 4 applicants");
            Character(people, "Elin Moss · Ranger", new Vector2(-1.15f, 1.35f), 0, "ov_ranger",
                "Example contractor fixture (level 1, Woodland Familiar).", "Phase 4 applicants");
            Character(people, "Nessa Quick · Rogue", new Vector2(1.15f, 1.35f), 2, "ov_rogue",
                "Example contractor fixture (level 1, Bold).", "Phase 4 applicants");
            Character(people, "Ivo Reed · Mage", new Vector2(1.9f, 1.75f), 1, "ov_mage",
                "Example contractor fixture (level 1, Poison Trained).", "Phase 4 applicants");
            Character(people, "Miner", new Vector2(-7.7f, 4.6f), 2, "ov_miner",
                "Worker placeholder. Workers keep their names and improve only through equipment.", "Phase 2 workers");
            Character(people, "Woodcutter", new Vector2(7.9f, 5.6f), 0, "ov_woodcutter",
                "Worker placeholder. Workers keep their names and improve only through equipment.", "Phase 2 workers");

            var walker = player.gameObject.AddComponent<DebugWalker>();
            walker.EditorSetup(player.Find("Visual"));
            var stack = player.gameObject.AddComponent<CarryStackFixture>();
            stack.EditorSetup(new List<Sprite>
            {
                ProjectSetup.LoadSprite("Stacks/stack_iron_ore"), ProjectSetup.LoadSprite("Stacks/stack_iron_ore"),
                ProjectSetup.LoadSprite("Stacks/stack_cedar_log"), ProjectSetup.LoadSprite("Stacks/stack_iron_ingot"),
                ProjectSetup.LoadSprite("Stacks/stack_cedar_lumber"), ProjectSetup.LoadSprite("Stacks/stack_herb"),
                ProjectSetup.LoadSprite("Stacks/stack_iron_sword"), ProjectSetup.LoadSprite("Stacks/stack_recovery_tonic"),
            }, 10, 12);

            // --- camera, light-independent rendering, controller ---
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 9f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Backdrop;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            camGo.transform.position = new Vector3(0, 0, -10f);
            camGo.AddComponent<FollowCamera>().EditorSetup(player);

            var controller = new GameObject("SandboxSceneController").AddComponent<SandboxSceneController>();
            controller.EditorSetup(cam, player);

            EditorSceneManager.SaveScene(scene, path);
        }

        // ------------------------------------------------------------------ ground

        private static void BuildGround(SandboxSettings settings, Dictionary<string, TileBase> tiles)
        {
            var gridGo = new GameObject("Grid");
            var grid = gridGo.AddComponent<Grid>();
            grid.cellLayout = GridLayout.CellLayout.Isometric;
            grid.cellSize = new Vector3(1f, 0.5f, 1f);

            var tmGo = new GameObject("Ground");
            tmGo.transform.SetParent(gridGo.transform, false);
            var tm = tmGo.AddComponent<Tilemap>();
            var tr = tmGo.AddComponent<TilemapRenderer>();
            tr.mode = TilemapRenderer.Mode.Individual;
            tr.sortingLayerName = "Ground";
            if (_unlit != null) tr.sharedMaterial = _unlit;

            var b = settings.plotBounds;
            var paths = new[]
            {
                // store front -> each destination (polyline segments)
                (new Vector2(0f, 1.5f), new Vector2(0f, -6.5f)),
                (new Vector2(0f, 0.6f), new Vector2(-7.2f, 0.0f)), (new Vector2(-7.2f, 0.0f), new Vector2(-8.6f, 4.4f)),
                (new Vector2(0f, 0.6f), new Vector2(7.2f, 0.0f)), (new Vector2(7.2f, 0.0f), new Vector2(8.8f, 4.6f)),
                (new Vector2(0f, -1.0f), new Vector2(-5.4f, -3.2f)), (new Vector2(0f, -1.0f), new Vector2(5.6f, -3.2f)),
            };

            for (var x = -40; x <= 40; x++)
            for (var y = -40; y <= 40; y++)
            {
                var cell = new Vector3Int(x, y, 0);
                Vector2 c = grid.GetCellCenterWorld(cell);
                if (c.x < b.xMin || c.x > b.xMax || c.y < b.yMin || c.y > b.yMax) continue;

                string key;
                var plaza = Mathf.Abs(c.x) / 3.6f + Mathf.Abs(c.y - 2.4f) / 2.0f < 1f;
                if (plaza) key = "plaza";
                else if (NearAnyPath(c, paths, 0.55f)) key = "path";
                else if (c.x < -6.4f && c.y > 2.2f) key = "dirt";
                else if (c.x > 6.2f && c.y > 2.2f) key = "forest_floor";
                else key = Hash01(x, y) < 0.22f ? "grass_b" : "grass_a";
                tm.SetTile(cell, tiles[key]);
            }
        }

        /// <summary>Deterministic per-cell noise (no visible diagonal banding).</summary>
        private static float Hash01(int x, int y)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        private static bool NearAnyPath(Vector2 p, (Vector2 a, Vector2 b)[] segments, float halfWidth)
        {
            foreach (var (a, bb) in segments)
            {
                var ab = bb - a;
                var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                if (Vector2.Distance(p, a + ab * t) <= halfWidth) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ objects

        private static SpriteRenderer Sprite(Transform parent, string name, string spritePath, Vector2 localPos, int order = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ProjectSetup.LoadSprite(spritePath);
            sr.spriteSortPoint = SpriteSortPoint.Pivot;
            sr.sortingOrder = order;
            if (_unlit != null) sr.sharedMaterial = _unlit;
            return sr;
        }

        private static void Building(Transform parent, string title, string sprite, Vector2 pos, Vector2 tapSize, string description, string phase)
        {
            var sr = Sprite(parent, title, "Buildings/" + sprite, pos);
            var col = sr.gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = tapSize;
            col.offset = new Vector2(0f, tapSize.y * 0.3f);
            sr.gameObject.AddComponent<Inspectable>().EditorSetup(title, description, phase);
            AddLabel(sr.transform, title, sr.sprite.bounds.max.y + 0.15f);
        }

        private static void Prop(Transform parent, string title, string sprite, Vector2 pos, string description, string phase, float scale = 1f)
        {
            var sr = Sprite(parent, title, sprite, pos);
            sr.transform.localScale = Vector3.one * scale;
            var col = sr.gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            var bounds = sr.sprite.bounds;
            col.size = bounds.size;
            col.offset = bounds.center;
            sr.gameObject.AddComponent<Inspectable>().EditorSetup(title, description, phase);
        }

        private static void Bell(Transform parent, Vector2 counterGroundPos, float counterHeight)
        {
            var root = new GameObject("Store Bell");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = counterGroundPos;
            root.AddComponent<SortingGroup>();
            var sr = Sprite(root.transform, "Visual", "Props/prop_bell", new Vector2(0f, counterHeight));
            var col = root.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.7f, 0.8f);
            col.offset = new Vector2(0f, counterHeight + 0.2f);
            var bell = root.AddComponent<BellInspectable>();
            bell.EditorSetup("Store Bell", "", "");
            bell.EditorSetupVisual(sr.transform);
        }

        private static Transform Character(Transform parent, string title, Vector2 pos, int skin, string overlay, string description, string phase, bool isPlayer = false)
        {
            var root = new GameObject(isPlayer ? "Player" : title);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.AddComponent<SortingGroup>();
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            Sprite(visual, "Base", $"Characters/char_base_{skin}", Vector2.zero, 0);
            Sprite(visual, "Overlay", $"Characters/{overlay}", Vector2.zero, 1);
            var col = root.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.7f, 1.1f);
            col.offset = new Vector2(0f, 0.5f);
            root.AddComponent<Inspectable>().EditorSetup(title, description, phase);
            return root.transform;
        }

        private static void AddLabel(Transform target, string text, float height)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(target, false);
            labelGo.transform.localPosition = new Vector3(0f, height, 0f);
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 3.2f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color32(250, 242, 226, 255);
            tmp.outlineWidth = 0.25f;
            tmp.outlineColor = new Color32(59, 42, 32, 255);
            tmp.rectTransform.sizeDelta = new Vector2(4f, 0.6f);
            tmp.sortingLayerID = SortingLayer.NameToID("Overlay");
            labelGo.SetActive(false);
            target.gameObject.AddComponent<WorldLabel>().EditorSetup(labelGo);
        }

        private static void Flowers(Transform parent, SandboxSettings settings)
        {
            var rnd = new System.Random(1234);
            var group = new GameObject("Flowers").transform;
            group.SetParent(parent, false);
            var placed = 0;
            for (var attempt = 0; attempt < 200 && placed < 26; attempt++)
            {
                var p = new Vector2((float)(rnd.NextDouble() * 22 - 11), (float)(rnd.NextDouble() * 16 - 8));
                // keep decals on open grass, away from the plaza and main paths
                if (Mathf.Abs(p.x) < 4.2f && p.y > -1.5f) continue;
                if (Mathf.Abs(p.y - 0.4f) < 0.9f || Mathf.Abs(p.x) < 0.9f) continue;
                if ((p.x < -6.0f || p.x > 6.0f) && p.y > 2.0f) continue;
                var sr = Sprite(group, "Flowers", $"Props/deco_flowers_{placed % 3}", p);
                sr.sortingLayerName = "Ground";
                sr.sortingOrder = 1;
                placed++;
            }
        }
    }
}
