using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Logistics;
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
    /// Phase 1 adds collision footprints, receiver zones and fixture piles/pads.
    /// </summary>
    public static class SandboxSceneBuilder
    {
        private const string UnlitMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";
        private static readonly Color Backdrop = new Color32(58, 86, 92, 255);

        private static Material _unlit;
        private static ContentCatalogue _catalogue;

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
            _catalogue = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            var settings = AssetDatabase.LoadAssetAtPath<SandboxSettings>(settingsPath);
            if (settings == null) throw new System.IO.FileNotFoundException("SandboxSettings not found", settingsPath);
            var tiles = ProjectSetup.EnsureTiles();

            new GameObject("SceneBootGuard").AddComponent<SceneBootGuard>();

            BuildGround(settings, tiles);
            BuildBoundary(settings);

            var world = new GameObject("World").transform;
            var buildings = Group(world, "Buildings");
            var nature = Group(world, "Nature");
            var props = Group(world, "Props");
            var logistics = Group(world, "Logistics");
            var people = Group(world, "People");

            // --- buildings (positions are footprint centres in world units) ---
            var store = Building(buildings, "Store", "bld_store", new Vector2(0f, 3.2f), new Vector2(3.1f, 2.6f),
                "Home base: inventory and display racks, Job Board, applicant meeting point and loadout hub. Open front: walk in and stand on the receiving strip to stock finished goods.",
                "Phase 3 racks and inventory · Phase 4 Job Board and applicants", footprint: null, station: ReceiverKind.Store);
            StoreWalls(store.transform);
            var smelter = Building(buildings, "Smelter", "bld_smelter", new Vector2(-7.2f, 0.6f), new Vector2(2.2f, 2.4f),
                "Receives raw ore, coal and gems into shared inventory.", "Phase 3 timed processing", new Vector2(2f, 2f), ReceiverKind.Smelter);
            Building(buildings, "Forge", "bld_forge", new Vector2(-5.4f, -3.8f), new Vector2(2.2f, 2.0f),
                "Covered work area. Crafts metal weapons and armor instantly from permanent blueprints. Finished goods appear on its output pad.",
                "Phase 3 crafting", new Vector2(2f, 2f));
            var mill = Building(buildings, "Lumbermill", "bld_lumbermill", new Vector2(7.2f, 0.6f), new Vector2(2.4f, 2.0f),
                "Receives logs into shared inventory.", "Phase 3 timed processing", new Vector2(2.5f, 2f), ReceiverKind.Lumbermill);
            Building(buildings, "Workbench", "bld_workbench", new Vector2(5.6f, -3.8f), new Vector2(1.7f, 1.8f),
                "Crafts bows and staves instantly. Finished goods appear on its output pad.", "Phase 3 crafting", new Vector2(1.5f, 1.5f));
            var apothecary = Building(buildings, "Apothecary", "bld_apothecary", new Vector2(-2.9f, -1.7f), new Vector2(2.2f, 2.2f),
                "Receives herbs into shared inventory. Potions are crafted here.", "Phase 3 crafting", new Vector2(2f, 2f), ReceiverKind.Apothecary);
            Building(buildings, "Trader Pad", "bld_trader_pad", new Vector2(3.0f, -2.1f), new Vector2(2.2f, 2.0f),
                "A periodic visitor sets up here to sell external materials and buy equipment at poor prices.", "Phase 6 Trader", new Vector2(2f, 2f));
            var mine = Building(buildings, "Mine", "bld_mine_mouth", new Vector2(-9.0f, 6.0f), new Vector2(4.2f, 2.8f),
                "One active production site. Workers strike the current face; you choose to continue this layer or reveal a deeper one.",
                "Phase 2 workers and depth decisions", footprint: null);
            Box(mine.transform, new Vector2(0f, 0.75f), new Vector2(3.8f, 1.3f));
            var gate = Building(buildings, "Forest", "bld_forest_gate", new Vector2(9.0f, 6.0f), new Vector2(2.2f, 1.9f),
                "Trail into the Forest. Workers chop the current clearing; Push Deeper reveals a new area further out.",
                "Phase 2 workers and area decisions", footprint: null);
            Circle(gate.transform, new Vector2(-0.85f, 0f), 0.12f);
            Circle(gate.transform, new Vector2(0.85f, 0f), 0.12f);

            // --- nature ---
            var treeSpots = new[]
            {
                new Vector2(7.0f, 7.0f), new Vector2(10.6f, 5.2f), new Vector2(8.2f, 8.1f), new Vector2(11.0f, 7.6f),
                new Vector2(6.2f, 4.9f), new Vector2(11.3f, 3.6f), new Vector2(9.6f, 8.6f), new Vector2(5.4f, 7.6f),
            };
            for (var i = 0; i < treeSpots.Length; i++)
            {
                var cedar = i % 2 == 0;
                var t = Prop(nature, cedar ? "Cedar Tree" : "Maple Tree", cedar ? "Nature/tree_cedar" : "Nature/tree_maple", treeSpots[i],
                    cedar ? "Placeholder cedar. Forest workers will claim individual trees." : "Placeholder maple. Deeper areas mix in more maple.",
                    "Phase 2");
                Circle(t.transform, Vector2.zero, 0.22f);
                t.gameObject.AddComponent<ObstructionFader>();
            }
            Prop(nature, "Herb Bush", "Nature/bush_herb", new Vector2(8.6f, 3.3f), "Herbs come as a bonus while chopping.", "Phase 2");
            Prop(nature, "Herb Bush", "Nature/bush_herb", new Vector2(10.2f, 2.5f), "Herbs come as a bonus while chopping.", "Phase 2");
            foreach (var (name, sprite, pos, desc) in new[]
                     {
                         ("Stone Face", "Nature/rock_stone", new Vector2(-11.0f, 3.5f), "Placeholder rock face (plain stone)."),
                         ("Iron Face", "Nature/rock_iron", new Vector2(-6.9f, 4.0f), "Placeholder rock face with iron flecks."),
                         ("Copper Face", "Nature/rock_copper", new Vector2(-10.2f, 2.3f), "Placeholder rock face with copper flecks."),
                     })
            {
                var r = Prop(nature, name, sprite, pos, desc, "Phase 2");
                Circle(r.transform, new Vector2(0f, 0.15f), 0.45f);
            }

            // --- store furniture ---
            Prop(props, "Weapon Rack", "Props/rack_weapons", new Vector2(-0.75f, 3.75f), "Shows a representative subset of stored weapons.", "Phase 3", 0.7f);
            Prop(props, "Armor Stand", "Props/rack_armor", new Vector2(0.0f, 4.15f), "Shows stored armor.", "Phase 3", 0.7f);
            Prop(props, "Potion Shelf", "Props/rack_potions", new Vector2(0.75f, 3.75f), "Shows stored potions.", "Phase 3", 0.7f);
            Bell(props, new Vector2(0.55f, 2.95f), 0.42f);
            Flowers(props);

            // --- PHASE 1 FIXTURES: receivers, raw piles and output pads ---
            Receiver(logistics, "receiver.smelter", ReceiverKind.Smelter, new Vector2(-6.35f, -0.1f), 0.6f, smelter.transform, "icon_iron_ore", true);
            Receiver(logistics, "receiver.lumbermill", ReceiverKind.Lumbermill, new Vector2(6.3f, -0.15f), 0.6f, mill.transform, "icon_cedar_log", true);
            Receiver(logistics, "receiver.apothecary", ReceiverKind.Apothecary, new Vector2(-2.15f, -2.45f), 0.55f, apothecary.transform, "icon_herb", true);
            Receiver(logistics, "receiver.store", ReceiverKind.Store, new Vector2(-0.55f, 2.95f), 0.55f, store.transform, "icon_iron_sword", false);

            Container(logistics, "container.mine_staging", "Mine Staging", ContainerKind.RawPile, new Vector2(-8.4f, 3.7f),
                "Test fixture: ore the miners will drop here in Phase 2. Walk close to pick it up automatically, then carry it to the Smelter.",
                Mats(("resource.iron_ore", 18), ("resource.copper_ore", 6), ("resource.gem", 1)), null);
            Container(logistics, "container.forest_staging", "Forest Staging", ContainerKind.RawPile, new Vector2(8.6f, 4.4f),
                "Test fixture: logs and herbs. Logs go to the Lumbermill, herbs to the Apothecary.",
                Mats(("resource.cedar_log", 16), ("resource.maple_log", 4), ("resource.herb", 6)), null);
            Container(logistics, "container.forge_pad", "Forge Output", ContainerKind.OutputPad, new Vector2(-4.3f, -4.55f),
                "Test fixture: finished goods waiting on the Forge output pad. Carry them to the Store.",
                null, new[] { "item.iron_sword", "item.iron_sword", "item.iron_dagger", "item.riveted_coat", "item.riveted_coat" });
            Container(logistics, "container.workbench_pad", "Workbench Output", ContainerKind.OutputPad, new Vector2(4.6f, -4.45f),
                "Test fixture: finished bows and staves.", null, new[] { "item.cedar_bow", "item.cedar_bow", "item.cedar_staff" });
            Container(logistics, "container.apothecary_pad", "Apothecary Output", ContainerKind.OutputPad, new Vector2(-3.85f, -2.45f),
                "Test fixture: finished potions.", null, new[] { "item.recovery_tonic", "item.recovery_tonic", "item.recovery_tonic" });

            // --- people ---
            var player = Character(people, "Supplier (you)", new Vector2(0f, 0f), 0, "ov_supplier",
                "You run the supply company. You walk, carry and craft; you never fight.", "", isPlayer: true);
            Character(people, "Bram Flint · Warrior", new Vector2(-1.9f, 1.75f), 1, "ov_warrior",
                "Example contractor fixture (level 1, Cautious). Independent; quotes their own fee.", "Phase 4 applicants");
            Character(people, "Elin Moss · Ranger", new Vector2(-1.15f, 1.35f), 0, "ov_ranger",
                "Example contractor fixture (level 1, Woodland Familiar).", "Phase 4 applicants");
            Character(people, "Nessa Quick · Rogue", new Vector2(1.15f, 1.35f), 2, "ov_rogue",
                "Example contractor fixture (level 1, Bold).", "Phase 4 applicants");
            Character(people, "Ivo Reed · Mage", new Vector2(1.9f, 1.75f), 1, "ov_mage",
                "Example contractor fixture (level 1, Poison Trained).", "Phase 4 applicants");
            Character(people, "Miner", new Vector2(-7.5f, 4.7f), 2, "ov_miner",
                "Worker placeholder. Workers keep their names and improve only through equipment.", "Phase 2 workers");
            Character(people, "Woodcutter", new Vector2(7.9f, 5.6f), 0, "ov_woodcutter",
                "Worker placeholder. Workers keep their names and improve only through equipment.", "Phase 2 workers");

            player.gameObject.AddComponent<PlayerMotor>().EditorSetup(player.Find("Visual"));
            player.gameObject.AddComponent<CarryStackView>();

            // --- camera and controller ---
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
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<FeedbackAudio>();

            var controller = new GameObject("SandboxSceneController").AddComponent<SandboxSceneController>();
            controller.EditorSetup(cam, player);

            EditorSceneManager.SaveScene(scene, path);
        }

        // ------------------------------------------------------------------ ground and boundary

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

        /// <summary>Invisible wall around the town plot (the motor also clamps).</summary>
        private static void BuildBoundary(SandboxSettings settings)
        {
            var go = new GameObject("TownBoundary") { layer = ProjectSetup.BlockerLayerIndex };
            var edge = go.AddComponent<EdgeCollider2D>();
            var b = settings.plotBounds;
            edge.points = new[]
            {
                new Vector2(b.xMin, b.yMin), new Vector2(b.xMax, b.yMin), new Vector2(b.xMax, b.yMax),
                new Vector2(b.xMin, b.yMax), new Vector2(b.xMin, b.yMin),
            };
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

        private static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

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

        /// <summary>
        /// Places a building with a tap target and, if <paramref name="footprint"/> is given, an isometric
        /// diamond collision footprint (a × b tiles) independent of the roof art (GDD 3).
        /// </summary>
        private static SpriteRenderer Building(Transform parent, string title, string sprite, Vector2 pos, Vector2 tapSize,
            string description, string phase, Vector2? footprint, ReceiverKind? station = null)
        {
            var sr = Sprite(parent, title, "Buildings/" + sprite, pos);
            var col = sr.gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = tapSize;
            col.offset = new Vector2(0f, tapSize.y * 0.3f);
            if (station.HasValue)
            {
                var s = sr.gameObject.AddComponent<StationInspectable>();
                s.EditorSetup(title, description, phase);
                s.EditorSetupStation(station.Value);
            }
            else sr.gameObject.AddComponent<Inspectable>().EditorSetup(title, description, phase);
            if (footprint.HasValue) Diamond(sr.transform, footprint.Value.x, footprint.Value.y, 0.92f);
            sr.gameObject.AddComponent<ObstructionFader>();
            AddLabel(sr.transform, title, sr.sprite.bounds.max.y + 0.15f);
            return sr;
        }

        private static GameObject Blocker(Transform parent, string name)
        {
            var go = new GameObject(name) { layer = ProjectSetup.BlockerLayerIndex };
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>Isometric footprint centred on the parent's pivot (footprint centre).</summary>
        private static void Diamond(Transform parent, float a, float b, float shrink)
        {
            var f = new Vector2(-(a - b) / 4f, -(a + b) / 8f);
            var r = f + new Vector2(a / 2f, a / 4f);
            var back = f + new Vector2((a - b) / 2f, (a + b) / 4f);
            var l = f + new Vector2(-b / 2f, b / 4f);
            var poly = Blocker(parent, "Footprint").AddComponent<PolygonCollider2D>();
            poly.points = new[] { f * shrink, r * shrink, back * shrink, l * shrink };
        }

        /// <summary>The Store is open-front: only its two back walls and the counter block movement.</summary>
        private static void StoreWalls(Transform store)
        {
            const float a = 3f, b = 3f, t = 0.18f;
            var f = new Vector2(0f, -(a + b) / 8f);
            var r = f + new Vector2(a / 2f, a / 4f);
            var back = f + new Vector2(0f, (a + b) / 4f);
            var l = f + new Vector2(-b / 2f, b / 4f);
            var walls = Blocker(store, "BackWalls");
            var inward = new Vector2(0f, -t);
            walls.AddComponent<PolygonCollider2D>().points = new[] { l, back, back + inward, l + inward + new Vector2(t, 0f) };
            walls.AddComponent<PolygonCollider2D>().points = new[] { back, r, r + inward - new Vector2(t, 0f), back + inward };
            // Counter along the front-right edge (matches the art).
            var counter = Blocker(store, "Counter").AddComponent<PolygonCollider2D>();
            var c0 = f + (r - f) * 0.15f;
            var along = (r - f).normalized * 1.6f * 0.5f * 1.118f;
            var depth = new Vector2(-0.2f, 0.1f);
            counter.points = new[] { c0, c0 + along, c0 + along + depth, c0 + depth };
        }

        private static void Circle(Transform parent, Vector2 offset, float radius)
        {
            var c = Blocker(parent, "Footprint").AddComponent<CircleCollider2D>();
            c.offset = offset;
            c.radius = radius;
        }

        private static void Box(Transform parent, Vector2 offset, Vector2 size)
        {
            var c = Blocker(parent, "Footprint").AddComponent<BoxCollider2D>();
            c.offset = offset;
            c.size = size;
        }

        private static SpriteRenderer Prop(Transform parent, string title, string sprite, Vector2 pos, string description, string phase, float scale = 1f)
        {
            var sr = Sprite(parent, title, sprite, pos);
            sr.transform.localScale = Vector3.one * scale;
            var col = sr.gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            var bounds = sr.sprite.bounds;
            col.size = bounds.size;
            col.offset = bounds.center;
            sr.gameObject.AddComponent<Inspectable>().EditorSetup(title, description, phase);
            return sr;
        }

        private static void Receiver(Transform parent, string id, ReceiverKind kind, Vector2 pos, float radius, Transform bump, string iconName, bool resourceIcon)
        {
            var root = new GameObject("Receiver " + kind);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;

            var decal = Sprite(root.transform, "ZoneDecal", "Props/receiver_zone", Vector2.zero);
            decal.transform.localScale = Vector3.one * radius;
            decal.sortingLayerName = "Ground";
            decal.sortingOrder = 2;
            decal.color = new Color(1, 1, 1, 0.4f);

            var bubble = Sprite(root.transform, "Bubble", "Props/ui_bubble", new Vector2(0f, 1.1f));
            bubble.sortingLayerName = "Overlay";
            bubble.sortingOrder = 1;
            var icon = Sprite(bubble.transform, "Icon", (resourceIcon ? "Icons/Resources/" : "Icons/Items/") + iconName, new Vector2(0f, 0.36f));
            icon.transform.localScale = Vector3.one * 0.62f;
            icon.sortingLayerName = "Overlay";
            icon.sortingOrder = 2;

            root.AddComponent<ReceiverView>().EditorSetup(id, kind, radius, decal, bubble, icon, bump);
        }

        private static void Container(Transform parent, string id, string title, ContainerKind kind, Vector2 pos, string description,
            List<WorldContainerView.ResourceAmount> materials, string[] itemIds)
        {
            var root = new GameObject(title);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.AddComponent<SortingGroup>();
            if (kind == ContainerKind.OutputPad) Sprite(root.transform, "Pad", "Props/prop_output_pad", Vector2.zero);
            else Sprite(root.transform, "Shadow", "Props/blob_shadow", new Vector2(0f, 0.05f)).transform.localScale = new Vector3(1.6f, 1.4f, 1f);

            var items = new List<ItemDefinition>();
            if (itemIds != null) foreach (var i in itemIds) items.Add(_catalogue.Get<ItemDefinition>(i));
            root.AddComponent<WorldContainerView>().EditorSetup(id, kind, 0.45f, materials, items);

            var col = root.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.0f, 0.9f);
            col.offset = new Vector2(0f, 0.3f);
            root.AddComponent<Inspectable>().EditorSetup(title, description, "Phase 1 test fixture");
        }

        private static List<WorldContainerView.ResourceAmount> Mats(params (string id, int qty)[] entries)
        {
            var list = new List<WorldContainerView.ResourceAmount>();
            foreach (var (id, qty) in entries)
                list.Add(new WorldContainerView.ResourceAmount { resource = _catalogue.Get<ResourceDefinition>(id), quantity = qty });
            return list;
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
            if (!isPlayer) Circle(root.transform, Vector2.zero, 0.2f);
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

        private static void Flowers(Transform parent)
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
