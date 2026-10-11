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
        /// <summary>The Mine pit's front cell and front corner (world), snapped to the tile grid at the town's front edge.</summary>
        private static Vector3Int _mineCell;
        private static Grid _grid;
        private static Vector2 _mineCorner;
        /// <summary>The pit's front corner sits on the town's bottom row of tiles, so the front cell is the edge itself.</summary>
        private static Tilemap _groundMap;
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
                "Phase 4 Job Board and applicants", footprint: null, station: ReceiverKind.Store);
            StoreWalls(store.transform);
            var smelter = Building(buildings, "Smelter", "bld_smelter", new Vector2(-14.5f, -1.5f), new Vector2(2.2f, 2.4f),
                "Receives raw ore, coal and gems, then smelts them into ingots automatically.", "", new Vector2(2f, 2f), ReceiverKind.Smelter, stationId: "station.smelter");
            Building(buildings, "Forge", "bld_forge", new Vector2(-5.4f, -3.8f), new Vector2(2.2f, 2.0f),
                "Covered work area. Crafts metal weapons and armor instantly from permanent blueprints. Finished goods appear on its output pad.",
                "", new Vector2(2f, 2f), stationId: "station.forge");
            var mill = Building(buildings, "Lumbermill", "bld_lumbermill", new Vector2(-11.6f, 3.2f), new Vector2(2.4f, 2.0f),
                "Receives logs, then saws them into lumber automatically.", "", new Vector2(2.5f, 2f), ReceiverKind.Lumbermill, stationId: "station.lumbermill");
            Building(buildings, "Workbench", "bld_workbench", new Vector2(5.6f, -3.8f), new Vector2(1.7f, 1.8f),
                "Crafts bows and staves instantly. Finished goods appear on its output pad.", "", new Vector2(1.5f, 1.5f), stationId: "station.workbench");
            var apothecary = Building(buildings, "Apothecary", "bld_apothecary", new Vector2(-2.9f, -1.7f), new Vector2(2.2f, 2.2f),
                "Receives herbs (no processing needed). Potions are crafted here.", "", new Vector2(2f, 2f), ReceiverKind.Apothecary, stationId: "station.apothecary");
            var traderPad = Building(buildings, "Trader Pad", "bld_trader_pad", new Vector2(3.0f, -2.1f), new Vector2(2.2f, 2.0f),
                "A periodic visitor sets up here to sell external materials and buy Store goods at poor prices.", "", new Vector2(2f, 2f));
            TraderStall(traderPad);
            var mine = MineDig(buildings);
            var gate = Building(buildings, "Forest", "bld_forest_gate", new Vector2(-15.5f, 3.0f), new Vector2(2.2f, 1.9f),
                "The frontier Forest, left of town and above the Mine's tunnels. Its trees grow tight: woodcutters cut their way in. " +
                "Continue Here cuts the next trees up this area; Push Deeper cuts into the next area to the left.",
                "", footprint: null, siteId: "site.forest");
            Circle(gate.transform, new Vector2(-0.85f, 0f), 0.12f);
            Circle(gate.transform, new Vector2(0.85f, 0f), 0.12f);

            // --- nature ---
            foreach (var (spot, cedar) in new[] { (new Vector2(5.4f, 8.2f), true), (new Vector2(11.4f, 5.0f), false), (new Vector2(7.2f, 8.8f), false) })
            {
                var t = Prop(nature, cedar ? "Cedar Tree" : "Maple Tree", cedar ? "Nature/tree_cedar" : "Nature/tree_maple", spot,
                    "Background tree outside the active clearing.", "");
                Circle(t.transform, Vector2.zero, 0.22f);
                t.gameObject.AddComponent<ObstructionFader>();
            }
            var pocket = ForestGrid(nature, settings);
            var chop = new GameObject("ChopSpot");
            chop.transform.SetParent(pocket.transform, false);
            var chopCol = chop.AddComponent<CircleCollider2D>();
            chopCol.isTrigger = true;
            chopCol.radius = 0.4f;
            chop.AddComponent<ForestChopSpot>();
            Prop(nature, "Herb Bush", "Nature/bush_herb", new Vector2(-9.5f, 5.0f), "Herbs come as a bonus while chopping.", "Phase 2");
            Prop(nature, "Herb Bush", "Nature/bush_herb", new Vector2(-11.5f, -0.9f), "Herbs come as a bonus while chopping.", "Phase 2");
            foreach (var (name, sprite, pos, desc) in new[]
                     {
                         ("Stone Face", "Nature/rock_stone", new Vector2(9.8f, -1.0f), "Placeholder rock face (plain stone)."),
                         ("Iron Face", "Nature/rock_iron", new Vector2(-6.9f, 4.0f), "Placeholder rock face with iron flecks."),
                         ("Copper Face", "Nature/rock_copper", new Vector2(10.6f, 1.6f), "Placeholder rock face with copper flecks."),
                     })
            {
                var r = Prop(nature, name, sprite, pos, desc, "Phase 2");
                Circle(r.transform, new Vector2(0f, 0.15f), 0.45f);
            }

            // --- store furniture ---
            Rack(props, "Weapon Rack", "Props/rack_weapons_empty", new Vector2(-0.75f, 3.75f), ItemCategory.Weapon,
                new List<Vector2> { new Vector2(-0.3f, 0.45f), new Vector2(0f, 0.45f), new Vector2(0.3f, 0.45f) });
            Rack(props, "Armor Stand", "Props/rack_armor_empty", new Vector2(0.0f, 4.15f), ItemCategory.Armor,
                new List<Vector2> { new Vector2(0f, 0.6f) });
            Rack(props, "Potion Shelf", "Props/rack_potions_empty", new Vector2(0.75f, 3.75f), ItemCategory.Potion,
                new List<Vector2> { new Vector2(-0.3f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.3f, 0.5f), new Vector2(-0.3f, 0.9f), new Vector2(0f, 0.9f), new Vector2(0.3f, 0.9f) });
            Bell(props, new Vector2(0.55f, 2.95f), 0.42f);
            // Phase 7: the Beacon trophy, hidden until the campaign finale succeeds.
            Prop(props, "Beacon Trophy", "Props/prop_beacon_trophy", new Vector2(-3.2f, 2.3f),
                "The relit Beacon's flame, carried home by your contractors. Ravenholm remembers.", "").gameObject.AddComponent<TrophyView>();
            Flowers(props);

            // --- PHASE 1 FIXTURES: receivers, raw piles and output pads ---
            Receiver(logistics, "receiver.smelter", ReceiverKind.Smelter, new Vector2(-13.65f, -2.2f), 0.6f, smelter.transform, "icon_iron_ore", true);
            Receiver(logistics, "receiver.lumbermill", ReceiverKind.Lumbermill, new Vector2(-12.5f, 2.45f), 0.6f, mill.transform, "icon_cedar_log", true);
            Receiver(logistics, "receiver.apothecary", ReceiverKind.Apothecary, new Vector2(-2.15f, -2.45f), 0.55f, apothecary.transform, "icon_herb", true);
            Receiver(logistics, "receiver.store", ReceiverKind.Store, new Vector2(-0.55f, 2.95f), 0.55f, store.transform, "icon_iron_sword", false);

            var mineStaging = Container(logistics, "container.mine_staging", "Mine Staging", ContainerKind.RawPile,
                (Vector2)_grid.GetCellCenterWorld(_mineCell + new Vector3Int(MineDigView.Size + 2, 3, 0)),
                "Ore lands here while you mine on a routine (capacity 96). Ore you mine by hand goes straight into your carry.", null, null, 96);
            var forestStaging = Container(logistics, "container.forest_staging", "Forest Staging", ContainerKind.RawPile, new Vector2(-17.6f, 2.0f),
                "The woodcutter drops logs and herbs here (capacity 96). Logs go to the Lumbermill, herbs to the Apothecary.", null, null, 96);
            Container(logistics, "container.forge_pad", "Forge Output", ContainerKind.OutputPad, new Vector2(-4.3f, -4.55f),
                "Crafted goods appear here (holds 6). Walk close to pick them up, then stock them at the Store.", null, null, 6);
            Container(logistics, "container.workbench_pad", "Workbench Output", ContainerKind.OutputPad, new Vector2(4.6f, -4.45f),
                "Crafted bows and staves appear here (holds 6).", null, null, 6);
            Container(logistics, "container.apothecary_pad", "Apothecary Output", ContainerKind.OutputPad, new Vector2(-3.85f, -2.45f),
                "Crafted potions appear here (holds 6).", null, null, 6);
            ProcessorBar(smelter.transform, "station.smelter", 2.75f);
            ProcessorBar(mill.transform, "station.lumbermill", 2.2f);

            // --- people ---
            var player = Character(people, "Supplier (you)", new Vector2(0f, 0f), 0, "ov_supplier",
                "You run the supply company. You walk, carry and craft; you never fight.", "", isPlayer: true);
            // Applicants gather at the Store front for the open posting (Phase 4).
            var crowd = new GameObject("Applicants").AddComponent<ApplicantCrowdView>();
            crowd.transform.SetParent(people, false);
            crowd.EditorSetup(new List<Vector2> { new Vector2(-1.9f, 1.75f), new Vector2(-1.15f, 1.35f), new Vector2(1.15f, 1.35f), new Vector2(1.9f, 1.75f) },
                new List<Sprite> { ProjectSetup.LoadSprite("Characters/char_base_0"), ProjectSetup.LoadSprite("Characters/char_base_1"), ProjectSetup.LoadSprite("Characters/char_base_2") });
            var mouth = mine.transform;   // miners follow the pit's ladder and blocks (MineDigView)
            // Solo play: the supplier is the only one working. While on a routine they are drawn by these figures instead.
            Worker(people, "You (mining)", "site.mine", "worker.player", 0, "ov_supplier", mineStaging, mouth, null);
            Worker(people, "You (woodcutting)", "site.forest", "worker.player", 0, "ov_supplier", forestStaging, null, pocket);
            SiteMarker(mine.transform, "site.mine", 2.4f);
            SiteMarker(gate.transform, "site.forest", 2.0f);

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
            _grid = grid;
            grid.cellLayout = GridLayout.CellLayout.Isometric;
            grid.cellSize = new Vector3(1f, 0.5f, 1f);

            var tmGo = new GameObject("Ground");
            tmGo.transform.SetParent(gridGo.transform, false);
            var tm = tmGo.AddComponent<Tilemap>();
            _groundMap = tm;
            var tr = tmGo.AddComponent<TilemapRenderer>();
            tr.mode = TilemapRenderer.Mode.Individual;
            tr.sortingLayerName = "Ground";
            if (_unlit != null) tr.sharedMaterial = _unlit;

            // One grid for everything (GDD 6): the town is a rectangle of cells, so its edges are straight cliff faces. The
            // Mine's shaft stands out from the town's left corner; the frontier continues the shaft's left face beyond the
            // town's upper-left edge, and the tunnels and the Forest run along it.
            var town = settings.townCells;
            _mineCell = new Vector3Int(settings.mineCell.x, settings.mineCell.y, 0);
            _mineCorner = (Vector2)grid.GetCellCenterWorld(_mineCell) - new Vector2(0f, 0.25f);
            bool InMine(Vector3Int cell) =>
                cell.x >= _mineCell.x && cell.x < _mineCell.x + MineDigView.Size &&
                cell.y >= _mineCell.y && cell.y < _mineCell.y + MineDigView.Size;
            var frontierY = town.yMax;   // first frontier row
            var paths = new[]
            {
                (new Vector2(0f, 1.5f), new Vector2(0f, -6.5f)),
                (new Vector2(0f, 0.6f), new Vector2(-7.2f, 0.0f)), (new Vector2(-7.2f, 0.0f), new Vector2(-13.6f, -2.2f)),
                (new Vector2(-13.6f, -2.2f), new Vector2(-18.4f, 0.3f)),
                (new Vector2(-7.2f, 0.0f), new Vector2(-12.5f, 2.45f)), (new Vector2(-12.5f, 2.45f), new Vector2(-17.6f, 2.0f)),
                (new Vector2(0f, 0.6f), new Vector2(7.2f, 0.0f)),
                (new Vector2(0f, -1.0f), new Vector2(-5.4f, -3.2f)), (new Vector2(0f, -1.0f), new Vector2(5.6f, -3.2f)),
            };
            var mineCentre = (Vector2)grid.GetCellCenterWorld(_mineCell + new Vector3Int(2, 2, 0));

            for (var x = _mineCell.x - 1; x <= Mathf.Max(town.xMax, _mineCell.x + settings.frontierWidthCells) + 1; x++)
            for (var y = town.yMin - 1; y <= frontierY + settings.frontierLengthCells; y++)
            {
                var cell = new Vector3Int(x, y, 0);
                Vector2 c = grid.GetCellCenterWorld(cell);
                if (InMine(cell)) continue;   // the pit: its own ground blocks (MineDigView)
                var inTown = x >= town.xMin && x < town.xMax && y >= town.yMin && y < town.yMax;
                var inFrontier = y >= frontierY && y < frontierY + settings.frontierLengthCells &&
                                 x >= _mineCell.x && x < _mineCell.x + settings.frontierWidthCells;
                if (inFrontier)
                {
                    // The Forest's ground (its trees are drawn by ForestGridView), with an open trail along the town.
                    tm.SetTile(cell, tiles[y < frontierY + 2 ? "path" : "forest_floor"]);
                    continue;
                }
                if (!inTown) continue;

                string key;
                var plaza = Mathf.Abs(c.x) / 3.6f + Mathf.Abs(c.y - 2.4f) / 2.0f < 1f;
                if (plaza) key = "plaza";
                else if (NearAnyPath(c, paths, 0.55f)) key = "path";
                else if (Vector2.Distance(c, mineCentre) < 3.2f) key = "dirt";   // worked ground around the Mine
                else if (c.x < -6.4f && c.y > 2.2f) key = "dirt";
                else key = Hash01(x, y) < 0.22f ? "grass_b" : "grass_a";
                tm.SetTile(cell, tiles[key]);
            }
        }

        /// <summary>Invisible wall around the town plot (the motor also clamps).</summary>
        private static void BuildBoundary(SandboxSettings settings)
        {
            var go = new GameObject("TownBoundary") { layer = ProjectSetup.BlockerLayerIndex };
            var edge = go.AddComponent<EdgeCollider2D>();
            var b = settings.WorldBounds;
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
            string description, string phase, Vector2? footprint, ReceiverKind? station = null, string siteId = null, string stationId = null)
        {
            var sr = Sprite(parent, title, "Buildings/" + sprite, pos);
            var col = sr.gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = tapSize;
            col.offset = new Vector2(0f, tapSize.y * 0.3f);
            if (siteId != null)
            {
                var site = sr.gameObject.AddComponent<SiteInspectable>();
                site.EditorSetup(title, description, phase);
                site.EditorSetupSite(siteId);
            }
            else if (station.HasValue || stationId != null)
            {
                var s = sr.gameObject.AddComponent<StationInspectable>();
                s.EditorSetup(title, description, phase);
                s.EditorSetupStation(stationId ?? "", station.HasValue, station ?? ReceiverKind.Store);
            }
            else sr.gameObject.AddComponent<Inspectable>().EditorSetup(title, description, phase);
            if (!string.IsNullOrEmpty(stationId))
            {
                // Phase 8: upgrade pennants along the roofline (hidden at level 1).
                var flags = new List<SpriteRenderer>();
                var top = sr.sprite.bounds.max.y;
                for (var i = 0; i < 2; i++)
                {
                    var f = Sprite(sr.transform, $"Pennant{i + 2}", "Props/prop_pennant", new Vector2(-0.35f + i * 0.35f, top - 0.35f), 5);
                    f.enabled = false;
                    flags.Add(f);
                }
                sr.gameObject.AddComponent<StationLevelView>().EditorSetup(stationId, flags);
            }
            if (footprint.HasValue) Diamond(sr.transform, footprint.Value.x, footprint.Value.y, 0.92f);
            sr.gameObject.AddComponent<ObstructionFader>();
            AddLabel(sr.transform, title, sr.sprite.bounds.max.y + 0.15f);
            return sr;
        }

        /// <summary>
        /// The Mine dug into the town's own ground (GDD 6): a 4×4 pit of ground blocks on the town grid at the
        /// exposed front edge. The pit top blocks movement; the site is tapped on the pit or its exposed faces.
        /// </summary>
        private static GameObject MineDig(Transform parent)
        {
            var go = new GameObject("Mine");
            go.transform.SetParent(parent, false);
            go.transform.position = _mineCorner;
            var tiles = ProjectSetup.EnsureTiles();
            go.AddComponent<MineDigView>().EditorSetup(_grid, _groundMap, _mineCell,
                new[] { tiles["block_0"], tiles["block_1"], tiles["block_2"] }, new[] { tiles["block_seam_0"], tiles["block_seam_1"], tiles["block_seam_2"] },
                tiles["slab"], tiles["sill"], ProjectSetup.LoadSprite("Props/prop_ladder"), ProjectSetup.LoadSprite("Props/prop_timber"),
                ProjectSetup.LoadSprite("Props/prop_lamp"), _unlit);
            go.AddComponent<MineProspectView>().EditorSetup(tiles["fleck"],
                new[] { ProjectSetup.LoadSprite("Props/fx_crack_1"), ProjectSetup.LoadSprite("Props/fx_crack_2"), ProjectSetup.LoadSprite("Props/fx_crack_3") },
                ProjectSetup.LoadSprite("Props/fx_ring"), _unlit);
            // Context actions in the mine: the ladder and the two tunnel ends of whichever level the player is on.
            foreach (var kind in new[] { MineActionSpot.Kind.Ladder, MineActionSpot.Kind.Tunnel, MineActionSpot.Kind.Shaft })
            {
                var spot = new GameObject($"MineAction{kind}");
                spot.transform.SetParent(go.transform, false);
                var col = spot.AddComponent<CircleCollider2D>();
                col.isTrigger = true;
                col.radius = 0.35f;
                spot.AddComponent<MineActionSpot>().EditorSetup(kind);
            }
            // Local corners of the pit's top diamond (front corner at the origin; +x right-up, +y left-up).
            Vector2 P(float u, float v) => new Vector2((u - v) * 0.5f, (u + v) * 0.25f);
            float n = MineDigView.Size;
            var tap = go.AddComponent<PolygonCollider2D>();
            tap.isTrigger = true;
            tap.points = new[] { P(0, 0) + new Vector2(0, -2.5f), P(n, 0) + new Vector2(0, -2.5f), P(n, 0), P(n, n), P(0, n), P(0, n) + new Vector2(0, -2.5f) };
            var site = go.AddComponent<SiteInspectable>();
            site.EditorSetup("Mine", "The Mine is dug straight into the ground. Each level is one miner deep and shows the layer it was dug in; " +
                                     "the levels below show darker. Miners clear a level block by block; then you choose to continue this layer or dig into the next. " +
                                     "Down is progress: Dig Deeper sinks the shaft through the next layer, three levels deep. Left is staying in a layer: " +
                                     "Continue Here digs that level's tunnel left, under the Forest. Climb down the ladder to walk the levels yourself.", "");
            site.EditorSetupSite("site.mine");
            var hole = Blocker(go.transform, "PitBlocker");
            hole.AddComponent<PolygonCollider2D>().points = new[] { P(0.1f, 0.1f), P(n - 0.1f, 0.1f), P(n - 0.1f, n - 0.1f), P(0.1f, n - 0.1f) };
            AddLabel(go.transform, "Mine", 2.6f);
            return go;
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

        private static Transform Container(Transform parent, string id, string title, ContainerKind kind, Vector2 pos, string description,
            List<WorldContainerView.ResourceAmount> materials, string[] itemIds, int capacity = 0)
        {
            var root = new GameObject(title);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.AddComponent<SortingGroup>();
            if (kind == ContainerKind.OutputPad) Sprite(root.transform, "Pad", "Props/prop_output_pad", Vector2.zero);
            else Sprite(root.transform, "Shadow", "Props/blob_shadow", new Vector2(0f, 0.05f)).transform.localScale = new Vector3(1.6f, 1.4f, 1f);

            var items = new List<ItemDefinition>();
            if (itemIds != null) foreach (var i in itemIds) items.Add(_catalogue.Get<ItemDefinition>(i));
            var view = root.AddComponent<WorldContainerView>();
            view.EditorSetup(id, kind, 0.45f, materials, items);
            view.EditorSetCapacity(capacity);

            var col = root.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.0f, 0.9f);
            col.offset = new Vector2(0f, 0.3f);
            root.AddComponent<Inspectable>().EditorSetup(title, description, "");
            return root.transform;
        }

        /// <summary>
        /// Racks stand against the Store's back walls. Their sort point sits just in front of the Store's
        /// own pivot so they draw over the back walls, while the visual is lifted back into place.
        /// </summary>
        private static void Rack(Transform parent, string title, string sprite, Vector2 pos, ItemCategory category, List<Vector2> slots)
        {
            const float sortY = 3.1f;
            var root = new GameObject(title);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector2(pos.x, sortY);
            root.AddComponent<SortingGroup>();
            var sr = Sprite(root.transform, "Visual", sprite, new Vector2(0f, pos.y - sortY));
            sr.transform.localScale = Vector3.one * 0.7f;
            var col = root.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.8f, 0.9f);
            col.offset = new Vector2(0f, pos.y - sortY + 0.45f);
            root.AddComponent<Inspectable>().EditorSetup(title,
                "Shows a representative subset of stocked items (strongest, then newest). Open the Store for the full inventory.", "");
            sr.gameObject.AddComponent<RackView>().EditorSetup(category, slots);
        }

        private static void ProcessorBar(Transform building, string stationId, float height)
        {
            var root = new GameObject("ProcessorBar");
            root.transform.SetParent(building, false);
            root.transform.localPosition = new Vector3(0f, height, 0f);
            var back = Sprite(root.transform, "Back", "UI/ui_chip", Vector2.zero);
            back.drawMode = SpriteDrawMode.Sliced;
            back.size = new Vector2(1.5f, 0.42f);
            back.sortingLayerName = "Overlay";
            back.sortingOrder = 1;
            var fill = Sprite(root.transform, "Fill", "UI/ui_chip", new Vector2(-0.38f, 0f));
            fill.drawMode = SpriteDrawMode.Sliced;
            fill.size = new Vector2(0.1f, 0.22f);
            fill.color = new Color32(232, 160, 70, 255);
            fill.sortingLayerName = "Overlay";
            fill.sortingOrder = 2;
            var icon = Sprite(root.transform, "Output", "Icons/Resources/icon_iron_ingot", new Vector2(-0.55f, 0f));
            icon.transform.localScale = Vector3.one * 0.38f;
            icon.sortingLayerName = "Overlay";
            icon.sortingOrder = 3;
            root.SetActive(false);
            building.gameObject.AddComponent<ProcessorView>().EditorSetup(stationId, root, fill, icon);
        }

        /// <summary>The Forest on the frontier: a dense grid of trees drawn by <see cref="ForestGridView"/> (GDD 7).</summary>
        private static ForestGridView ForestGrid(Transform parent, SandboxSettings settings)
        {
            var root = new GameObject("Forest Frontier");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<ForestGridView>();
            var frontierY = settings.townCells.yMax;
            view.EditorSetup(_grid, _groundMap, new Vector2Int(settings.mineCell.x, frontierY + 2), new Vector2Int(settings.mineCell.x + 7, frontierY),
                ProjectSetup.LoadSprite("Nature/tree_cedar"), ProjectSetup.LoadSprite("Nature/tree_maple"),
                ProjectSetup.LoadSprite("Nature/tree_stump"), new List<ResourceDefinition> { _catalogue.Get<ResourceDefinition>("resource.maple_log") },
                new List<SpeciesSprite>
                {
                    new SpeciesSprite { resource = _catalogue.Get<ResourceDefinition>("resource.ironwood_log"), sprite = ProjectSetup.LoadSprite("Nature/tree_ironwood") },
                }, _unlit);
            return view;
        }

        private static void Worker(Transform parent, string title, string siteId, string workerId, int skin, string overlay,
            Transform staging, Transform workPoint, ForestGridView pocket)
        {
            var root = new GameObject(title);
            root.transform.SetParent(parent, false);
            root.transform.position = staging.position + new Vector3(0.55f, -0.1f, 0f);
            root.AddComponent<SortingGroup>();
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            Sprite(visual, "Base", $"Characters/char_base_{skin}", Vector2.zero, 0);
            Sprite(visual, "Overlay", $"Characters/{overlay}", Vector2.zero, 1);
            var bubble = Sprite(root.transform, "Bubble", "Props/ui_bubble", new Vector2(0f, 1.25f));
            bubble.transform.localScale = Vector3.one * 0.8f;
            bubble.sortingOrder = 20;
            var icon = Sprite(bubble.transform, "Icon", "UI/Icons/ic_unknown", new Vector2(0f, 0.36f));
            icon.transform.localScale = Vector3.one * 0.55f;
            icon.sortingOrder = 21;
            bubble.gameObject.SetActive(false);
            var col = root.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.7f, 1.1f);
            col.offset = new Vector2(0f, 0.5f);
            var view = root.AddComponent<WorkerView>();
            view.EditorSetup(siteId, workerId, visual, staging, workPoint, pocket, bubble, icon,
                ProjectSetup.LoadSprite("UI/Icons/ic_unknown"), ProjectSetup.LoadSprite("UI/Icons/ic_warning"));
            var insp = root.AddComponent<WorkerInspectable>();
            insp.EditorSetup(title, "Worker.", "");
            insp.EditorSetupWorker(view);
        }

        /// <summary>Phase 6: the visiting Trader and goods, shown only while the Trader is in town.</summary>
        private static void TraderStall(SpriteRenderer pad)
        {
            var generic = pad.GetComponent<Inspectable>();
            var title = generic.Title;
            Object.DestroyImmediate(generic);
            var stall = new GameObject("Stall");
            stall.transform.SetParent(pad.transform, false);
            stall.AddComponent<SortingGroup>();
            Sprite(stall.transform, "Base", "Characters/char_base_1", new Vector2(-0.45f, 0.1f), 0);
            Sprite(stall.transform, "Overlay", "Characters/ov_supplier", new Vector2(-0.45f, 0.1f), 1);
            Sprite(stall.transform, "Goods", "Props/pile_lumber", new Vector2(0.45f, 0.05f), 0);
            Sprite(stall.transform, "Ingots", "Props/pile_ingots", new Vector2(0.15f, -0.2f), 1);
            var coin = Sprite(stall.transform, "Coin", "UI/Icons/ic_coin", new Vector2(-0.45f, 1.45f), 3);
            coin.transform.localScale = Vector3.one * 0.6f;
            stall.SetActive(false);
            var trader = pad.gameObject.AddComponent<TraderInspectable>();
            trader.EditorSetup(title, "A periodic visitor sets up here to sell external materials and buy Store goods at poor prices.", "");
            trader.EditorSetupTrader(stall);
        }

        private static void SiteMarker(Transform site, string siteId, float height)
        {
            var root = new GameObject("SiteMarker");
            root.transform.SetParent(site, false);
            var bubbleRoot = new GameObject("Bubble");
            bubbleRoot.transform.SetParent(root.transform, false);
            bubbleRoot.transform.localPosition = new Vector3(0f, height, 0f);
            var panel = Sprite(bubbleRoot.transform, "Back", "UI/ui_chip", Vector2.zero);
            panel.drawMode = SpriteDrawMode.Sliced;
            panel.size = new Vector2(3.0f, 0.62f);
            panel.sortingLayerName = "Overlay";
            panel.sortingOrder = 3;
            var icon = Sprite(bubbleRoot.transform, "Icon", "UI/Icons/ic_unknown", new Vector2(-1.15f, 0.02f));
            icon.transform.localScale = Vector3.one * 0.85f;
            icon.sortingLayerName = "Overlay";
            icon.sortingOrder = 4;
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(bubbleRoot.transform, false);
            labelGo.transform.localPosition = new Vector3(0.25f, 0f, 0f);
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.fontSize = 2.6f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color32(59, 42, 32, 255);
            tmp.rectTransform.sizeDelta = new Vector2(2.6f, 0.5f);
            tmp.sortingLayerID = SortingLayer.NameToID("Overlay");
            tmp.sortingOrder = 5;
            bubbleRoot.SetActive(false);
            root.AddComponent<SiteMarkerView>().EditorSetup(siteId, bubbleRoot, icon, tmp,
                ProjectSetup.LoadSprite("UI/Icons/ic_unknown"), ProjectSetup.LoadSprite("UI/Icons/ic_warning"));
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
