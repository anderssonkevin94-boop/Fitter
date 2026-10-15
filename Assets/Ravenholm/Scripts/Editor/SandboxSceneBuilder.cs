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
        private static readonly Color Valley = new Color32(112, 155, 92, 255);

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
            // The shop (Shop Titans style, 2026-10-15): drawn from its grid of tiles; furniture is placed in build mode.
            var shop = Shop(buildings);
            var smelter = Building(buildings, "Smelter", "bld_smelter", new Vector2(-14.5f, -1.5f), new Vector2(2.2f, 2.4f),
                "Receives raw ore, coal and gems, then smelts them into ingots automatically.", "", new Vector2(2f, 2f), ReceiverKind.Smelter, stationId: "station.smelter");
            Building(buildings, "Forge", "bld_forge", new Vector2(-5.4f, -3.8f), new Vector2(2.2f, 2.0f),
                "Covered work area. Makes metal weapons, armour, helmets and shields from permanent blueprints. Finished goods appear on its output pad.",
                "", new Vector2(2f, 2f), stationId: "station.forge");
            var mill = Building(buildings, "Lumbermill", "bld_lumbermill", new Vector2(-11.6f, 3.2f), new Vector2(2.4f, 2.0f),
                "Receives logs, then saws them into lumber automatically.", "", new Vector2(2.5f, 2f), ReceiverKind.Lumbermill, stationId: "station.lumbermill");
            Building(buildings, "Workshop", "bld_workbench", new Vector2(5.6f, -3.8f), new Vector2(1.7f, 1.8f),
                "Makes bows, staves, wooden shields, leather caps and light armour. Finished goods appear on its output pad.", "", new Vector2(1.5f, 1.5f), stationId: "station.workbench");
            var apothecary = Building(buildings, "Apothecary", "bld_apothecary", new Vector2(-2.9f, -1.7f), new Vector2(2.2f, 2.2f),
                "Receives herbs (no processing needed). Potions are crafted here.", "", new Vector2(2f, 2f), ReceiverKind.Apothecary, stationId: "station.apothecary");
            var traderPad = Building(buildings, "Trader Pad", "bld_trader_pad", new Vector2(3.0f, -2.1f), new Vector2(2.2f, 2.0f),
                "A periodic visitor sets up here to sell external materials and buy Store goods at poor prices.", "", new Vector2(2f, 2f));
            TraderStall(traderPad);
            traderPad.gameObject.AddComponent<CampAnchored>();   // the Trader comes along to each camp (the journey, step 4)
            MineDig(buildings);

            // --- nature ---
            var pocket = ForestGrid(nature, settings);
            var chop = new GameObject("ChopSpot");
            chop.transform.SetParent(pocket.transform, false);
            var chopCol = chop.AddComponent<CircleCollider2D>();
            chopCol.isTrigger = true;
            chopCol.radius = 0.4f;
            chop.AddComponent<ForestChopSpot>();
            // The Forest's ground has height (hills and ridges): its blocks, and the button to dig its rock.
            Sprite Block(string name) => ProjectSetup.LoadSprite("Painted/Blocks/block_" + name);
            pocket.EditorSetupTerrain(Block("grass"), Block("topsoil"), Block("mossy"), Block("slate"));
            pocket.EditorSetupHighlight(ProjectSetup.LoadSprite("Props/fx_block_highlight"));
            var rock = new GameObject("RockSpot");
            rock.transform.SetParent(pocket.transform, false);
            var rockCol = rock.AddComponent<CircleCollider2D>();
            rockCol.isTrigger = true;
            rockCol.radius = 0.4f;
            rock.AddComponent<ForestRockSpot>();
            Prop(nature, "Herb Bush", "Nature/bush_herb", new Vector2(-9.5f, 5.0f), "Herbs come as a bonus while chopping.", "Phase 2");
            Prop(nature, "Herb Bush", "Nature/bush_herb", new Vector2(-11.5f, -0.9f), "Herbs come as a bonus while chopping.", "Phase 2");
            // Camp 1 (2026-10-15): no rock faces or outcrops in the clearing; stone and ore come from the hills around it.

            // The valley view beyond the north cliff, with the castle on the far ridge (Astra, 2026-10-15). The placeholder
            // castle at the road's end is gone; adventurers come and go by the cliff path.
            var pathTop = Horizon(world, settings);
            // Phase 7: the Beacon trophy, hidden until the campaign finale succeeds.
            Prop(props, "Beacon Trophy", "Props/prop_beacon_trophy", new Vector2(-3.2f, 2.3f),
                "The relit Beacon's flame, carried home by your contractors. Ravenholm remembers.", "").gameObject.AddComponent<TrophyView>();
            props.Find("Beacon Trophy").gameObject.AddComponent<CampAnchored>();
            Flowers(props);

            // --- PHASE 1 FIXTURES: receivers, raw piles and output pads ---
            Receiver(logistics, "receiver.smelter", ReceiverKind.Smelter, new Vector2(-13.65f, -2.2f), 0.6f, smelter.transform, "icon_iron_ore", true);
            Receiver(logistics, "receiver.lumbermill", ReceiverKind.Lumbermill, new Vector2(-12.5f, 2.45f), 0.6f, mill.transform, "icon_cedar_log", true);
            Receiver(logistics, "receiver.apothecary", ReceiverKind.Apothecary, new Vector2(-2.15f, -2.45f), 0.55f, apothecary.transform, "icon_herb", true);
            Receiver(logistics, "receiver.store", ReceiverKind.Store, ShopEntrance(), 0.55f, shop, "icon_iron_sword", false);

            // The sites' staging piles stay as places for leftovers (old saves, overflow) but show nothing while empty:
            // there is no AFK gathering, everything you gather goes into your carry (2026-10-15).
            Container(logistics, "container.mine_staging", "Mine Staging", ContainerKind.RawPile,
                (Vector2)_grid.GetCellCenterWorld(_mineCell + new Vector3Int(MineEntry + 2, 3, 0)), null, null, null, hidden: true);
            Container(logistics, "container.forest_staging", "Forest Staging", ContainerKind.RawPile, new Vector2(-16.0f, 0.6f),
                null, null, null, hidden: true);
            Container(logistics, "container.forge_pad", "Forge Output", ContainerKind.OutputPad, new Vector2(-4.3f, -4.55f),
                "Crafted goods appear here (holds 6). Walk close to pick them up, then stock them at the Store.", null, null, 6);
            Container(logistics, "container.workbench_pad", "Workshop Output", ContainerKind.OutputPad, new Vector2(4.6f, -4.45f),
                "Crafted bows and staves appear here (holds 6).", null, null, 6);
            Container(logistics, "container.apothecary_pad", "Apothecary Output", ContainerKind.OutputPad, new Vector2(-3.85f, -2.45f),
                "Crafted potions appear here (holds 6).", null, null, 6);
            logistics.Find("Mine Staging").gameObject.AddComponent<CampAnchored>();
            logistics.Find("Forest Staging").gameObject.AddComponent<CampAnchored>();
            // Moving on (the journey, step 4): the sign in the next camp's clearing, and the drive up the road.
            var sign = Sprite(world, "Camp Sign", "Wagon/camp_sign", Vector2.zero);
            sign.gameObject.AddComponent<SpriteOutline>();
            var signCol = sign.gameObject.AddComponent<BoxCollider2D>();
            signCol.isTrigger = true;
            signCol.size = new Vector2(1.2f, 1.6f);
            signCol.offset = new Vector2(0f, 0.6f);
            sign.gameObject.AddComponent<MakeCampSpot>();
            new GameObject("Drive").AddComponent<DriveView>().EditorSetup(ProjectSetup.LoadSprite("Wagon/ox_walk"), _unlit);
            ProcessorBar(smelter.transform, "station.smelter", 2.75f);
            ProcessorBar(mill.transform, "station.lumbermill", 2.2f);
            // The journey, step 2 (2026-10-15): each station can be set up elsewhere at the camp, its marker and pad with it.
            Site(buildings, "Smelter", "station.smelter", logistics, "Receiver Smelter");
            Site(buildings, "Lumbermill", "station.lumbermill", logistics, "Receiver Lumbermill");
            Site(buildings, "Apothecary", "station.apothecary", logistics, "Receiver Apothecary", "Apothecary Output");
            Site(buildings, "Forge", "station.forge", logistics, "Forge Output");
            Site(buildings, "Workshop", "station.workbench", logistics, "Workshop Output");
            // The Kitchen (the crafting expansion, 2026-10-16): it arrives as a kit, to be set up in build mode.
            Building(buildings, "Kitchen", "bld_kitchen", new Vector2(8.6f, -2.6f), new Vector2(2.2f, 2.2f),
                "Receives foraged berries and mushrooms. Cooks food for the road.", "", new Vector2(2f, 2f), ReceiverKind.Kitchen, stationId: "station.kitchen");
            Receiver(logistics, "receiver.kitchen", ReceiverKind.Kitchen, new Vector2(9.35f, -3.35f), 0.55f, buildings.Find("Kitchen"), "icon_berries", true);
            Container(logistics, "container.kitchen_pad", "Kitchen Output", ContainerKind.OutputPad, new Vector2(7.9f, -3.3f),
                "Cooked food appears here (holds 6).", null, null, 6);
            Site(buildings, "Kitchen", "station.kitchen", logistics, "Receiver Kitchen", "Kitchen Output");

            // --- people ---
            var player = Character(people, "Supplier (you)", new Vector2(0f, 0f), 0, "ov_supplier",
                "You run the supply company. You walk, carry and craft; you never fight.", "", isPlayer: true);
            // Applicants gather at the Store front for the open posting (Phase 4).
            var crowd = new GameObject("Applicants").AddComponent<ApplicantCrowdView>();
            crowd.transform.SetParent(people, false);
            crowd.EditorSetup(new List<Vector2> { new Vector2(-1.9f, 1.75f), new Vector2(-1.15f, 1.35f), new Vector2(1.15f, 1.35f), new Vector2(1.9f, 1.75f) },
                new List<Sprite> { ProjectSetup.LoadSprite("Characters/char_base_0"), ProjectSetup.LoadSprite("Characters/char_base_1"), ProjectSetup.LoadSprite("Characters/char_base_2") });
            // For now every NPC is the blond citizen, walking exactly like the supplier (Kevin, 2026-10-15).
            crowd.EditorSetupCitizen(Citizen(), AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ProjectSetup.SupplierAnimationsPath));
            crowd.EditorSetupDeparture(pathTop);

            Animate(player.Find("Visual"));
            player.gameObject.AddComponent<PlayerMotor>().EditorSetup(player.Find("Visual"));
            player.gameObject.AddComponent<CarryStackView>();
            player.gameObject.AddComponent<SpriteOutline>();   // the game-drawn outline, for outline-free art

            // --- camera and controller ---
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 9f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Valley;   // past the ridge's edges: the valley far below, the near view layer's own green
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
            var camp = settings.camp;
            if (camp == null) throw new System.InvalidOperationException("SandboxSettings has no camp (run setup)");
            var cb = camp.Bounds;
            var paths = new[]
            {
                (new Vector2(0f, 1.5f), new Vector2(0f, -6.5f)),
                (new Vector2(0f, 0.6f), new Vector2(-7.2f, 0.0f)), (new Vector2(-7.2f, 0.0f), new Vector2(-13.6f, -2.2f)),
                (new Vector2(-13.6f, -2.2f), new Vector2(-18.4f, 0.3f)),
                (new Vector2(-7.2f, 0.0f), new Vector2(-12.5f, 2.45f)), (new Vector2(-12.5f, 2.45f), new Vector2(-17.6f, 2.0f)),
                (new Vector2(0f, 0.6f), new Vector2(7.2f, 0.0f)),
                (new Vector2(0f, -1.0f), new Vector2(-5.4f, -3.2f)), (new Vector2(0f, -1.0f), new Vector2(5.6f, -3.2f)),
            };

            // The journey (Kevin, 2026-10-15): a long, wide, winding mountain top running forward toward the castle. The
            // camp's clearing (round the old town), the forest and hills ahead with the old road wandering through them,
            // a gate across the ridge, and the next camp's clearing. Nothing beyond the ridge's ragged edges.
            var ridge = camp.Ridge;
            for (var x = cb.xMin; x < cb.xMax; x++)
            for (var y = cb.yMin; y < cb.yMax; y++)
            {
                if (!ridge.InLand(x, y)) continue;
                var cell = new Vector3Int(x, y, 0);
                Vector2 c = grid.GetCellCenterWorld(cell);
                var inTown = ridge.InClearing(x, y) && x >= town.xMin && x < town.xMax && y >= town.yMin && y < town.yMax;
                if (!inTown)
                {
                    string ground;
                    if (ridge.InClearing(x, y)) ground = GrassKey(x, y);   // the next camp's clearing
                    else if (ridge.OnRoad(x, y))
                    {
                        // The old cobble road, mostly overgrown: stretches of cobble, then worn earth, then forest floor.
                        var r = Hash01(x + 313, y + 17) * 0.5f + SmoothNoise(x + 40, y, 4f) * 0.5f;
                        ground = r < 0.38f ? "plaza" : r < 0.6f ? PathKey(x, y) : "forest_floor_b";
                    }
                    else ground = Hash01(x + 911, y) < 0.12f ? "forest_floor_b" : "forest_floor";   // trees and hills: ForestGridView
                    tm.SetTile(cell, tiles[ground]);
                    continue;
                }

                string key;
                var plaza = Mathf.Abs(c.x) / 3.6f + Mathf.Abs(c.y - 2.4f) / 2.0f < 1f;
                if (plaza) key = "plaza";
                else if (NearAnyPath(c, paths, 0.55f)) key = PathKey(x, y);
                else key = GrassKey(x, y);
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
        /// <summary>
        /// Grass broken up so the tile never repeats in a grid: mostly plain, tufts scattered on their own, and flowers
        /// in loose patches (smooth noise), so a meadow reads as a meadow.
        /// </summary>
        private static string GrassKey(int x, int y)
        {
            var patch = SmoothNoise(x, y, 5f);
            var r = Hash01(x, y);
            if (patch > 0.74f && r < 0.4f) return "grass_b";    // a small flower patch
            if (r < 0.14f) return "grass_c";                   // tufts
            if (r > 0.99f) return "grass_b";                   // a stray flower
            return "grass_a";
        }

        /// <summary>Paths mostly plain, with pebbles and the odd set of footprints.</summary>
        private static string PathKey(int x, int y)
        {
            var r = Hash01(x + 4057, y + 77);
            return r < 0.18f ? "path_b" : r < 0.26f ? "path_c" : "path";
        }

        /// <summary>Value noise in 0..1 on a grid of <paramref name="scale"/> cells, smoothly interpolated.</summary>
        private static float SmoothNoise(int x, int y, float scale)
        {
            float fx = x / scale, fy = y / scale;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = Mathf.SmoothStep(0f, 1f, fx - x0), ty = Mathf.SmoothStep(0f, 1f, fy - y0);
            float a = Hash01(x0 * 31 + 7, y0 * 17 + 3), b = Hash01((x0 + 1) * 31 + 7, y0 * 17 + 3);
            float c = Hash01(x0 * 31 + 7, (y0 + 1) * 17 + 3), d = Hash01((x0 + 1) * 31 + 7, (y0 + 1) * 17 + 3);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

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
            string description, string phase, Vector2? footprint, ReceiverKind? station = null, string stationId = null)
        {
            var sr = Sprite(parent, title, "Buildings/" + sprite, pos);
            sr.gameObject.AddComponent<SpriteOutline>();   // the game's outline (outline-free art)
            var col = sr.gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = tapSize;
            col.offset = new Vector2(0f, tapSize.y * 0.3f);
            if (station.HasValue || stationId != null)
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
            // The Mine is dug, block by block, into the ground at the town's left corner and under the frontier (dig slab).
            var slabRoot = new GameObject("MineSlab");
            slabRoot.transform.SetParent(parent, false);
            var tiles = ProjectSetup.EnsureTiles();
            slabRoot.AddComponent<MineSlabView>().EditorSetup(_grid, _groundMap, new Vector2Int(_mineCell.x, _mineCell.y),
                new[] { tiles["halfblock_outline_0"], tiles["halfblock_outline_1"], tiles["halfblock_outline_2"] }, tiles["fleck_half"],
                ProjectSetup.LoadSprite("Props/fx_block_highlight"),
                new[] { ProjectSetup.LoadSprite("Props/fx_crack_1"), ProjectSetup.LoadSprite("Props/fx_crack_2"), ProjectSetup.LoadSprite("Props/fx_crack_3") },
                ProjectSetup.LoadSprite("Props/fx_ring"), _unlit);
            Sprite Block(string name) => ProjectSetup.LoadSprite("Painted/Blocks/block_" + name);
            slabRoot.GetComponent<MineSlabView>().EditorSetupBuild(ProjectSetup.LoadSprite("Props/prop_scaffold"),
                ProjectSetup.LoadSprite("Props/prop_pulley"), ProjectSetup.LoadSprite("Props/prop_lift_platform"));
            slabRoot.GetComponent<MineSlabView>().EditorSetupBlocks(Block("grass"), new[] { Block("topsoil"), Block("topsoil_pebbles") },
                new[] { Block("sandstone"), Block("red_clay"), Block("slate"), Block("mossy"), Block("purple"), Block("ochre"), Block("basalt"), Block("limestone") });
            // Camp 1 (2026-10-15): no quarry, so no dig spot; the slab view still draws the ground's edges and the earth
            // under the hills (the Forest's rock is the Mine's surface).

            // An anchor beside the entry (its yellow-flag signpost went with the routines, 2026-10-15).
            var go = new GameObject("Mine");
            go.transform.SetParent(parent, false);
            go.transform.position = _grid.GetCellCenterWorld(_mineCell + new Vector3Int(QuarryDepth + 1, 1, 0));   // beside the quarry
            return go;
        }

        /// <summary>Size of the Mine's entry (the ground that can be broken into), matching the dig slab's default.</summary>
        private const int MineEntry = 4;

        /// <summary>The Mine is an open-pit quarry: its plot (dug from above) runs this far back from the cliff edge...</summary>
        private const int QuarryDepth = 5;

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

        /// <summary>World position of the shop lot's front corner (tile 0, 0).</summary>
        private static readonly Vector2 ShopOrigin = new Vector2(0f, 2.45f);

        /// <summary>The shop: a <see cref="ShopView"/> that draws itself from the shop's grid (placeholder shell art).</summary>
        private static Transform Shop(Transform parent)
        {
            var go = new GameObject("Shop");
            go.transform.SetParent(parent, false);
            // Astra's pieces where they exist (step 1, 2026-10-15), the placeholders otherwise.
            Sprite Art(string astra, string placeholder) => OptionalSprite("Shop/" + astra) ?? (placeholder != null ? ProjectSetup.LoadSprite(placeholder) : null);
            var view = go.AddComponent<ShopView>();
            view.EditorSetup(ShopOrigin,
                new[] { Art("floor_1", "Painted/Ground/ground_planks"), Art("floor_2", "Painted/Ground/ground_planks"), Art("floor_3", "Painted/Ground/ground_planks") },
                Art("floor_plot", "Painted/Ground/ground_dirt_dug"),
                Art("plot_sign", "Shop/plot_sign"),
                Art("wall_plain_left", "Shop/wall_tall_left"), Art("wall_plain_right", "Shop/wall_tall_right"),
                Art("stub_plain_left", "Shop/wall_low_left"), Art("stub_plain_right", "Shop/wall_low_right"),
                Art("post_tall", "Shop/post_tall_placeholder"), Art("post_low", "Shop/post_low_placeholder"),
                Art("tile_ok", "Shop/tile_ok"), Art("tile_blocked", "Shop/tile_blocked"), _unlit, ProjectSetup.BlockerLayerIndex);
            view.EditorSetupVariants(Art("wall_window_left", null), Art("wall_window_right", null), Art("stub_entrance_left", null), Art("stub_entrance_right", null));
            view.EditorSetupWallKinds(Art("wall_door_left", null), Art("wall_door_right", null), Art("wall_rail_left", null), Art("wall_rail_right", null));
            // The wagon shop (the journey, step 3): placeholders until Astra's ox and wagon land.
            Sprite[] Levels(string name) => new[] { ProjectSetup.LoadSprite($"Wagon/{name}".Replace("#", "1")), ProjectSetup.LoadSprite($"Wagon/{name}".Replace("#", "2")),
                ProjectSetup.LoadSprite($"Wagon/{name}".Replace("#", "3")) };
            view.EditorSetupWagon(Levels("wagon_parked_#"), Levels("carpet_#"), Levels("carpet_edge_#_a"), Levels("carpet_edge_#_b"));
            return go.transform;
        }

        private static Sprite OptionalSprite(string relativeNoExt)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderSpriteImporter.ArtRoot + relativeNoExt + ".png");
        }

        /// <summary>The middle of the tiles just inside the shop's entrance (where goods are stocked).</summary>
        private static Vector2 ShopEntrance()
        {
            var tiles = _catalogue.Shop.EntranceTiles();
            var sum = Vector2.zero;
            foreach (var e in tiles) sum += ShopOrigin + FurnitureView.AxisA * (e.x + 0.5f) + FurnitureView.AxisB * (e.y + 0.5f);
            return sum / tiles.Count;
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
            sr.gameObject.AddComponent<SpriteOutline>();   // outlined only if its sprite is outline-free art
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

        /// <summary>Makes a station movable at the camp: its footprint in cells (from content) and what moves with it.</summary>
        private static void Site(Transform buildings, string name, string stationId, Transform logistics, params string[] moveWith)
        {
            var building = buildings.Find(name);
            var def = _catalogue.Get<StationDefinition>(stationId);
            var attached = new List<Transform>();
            foreach (var n in moveWith)
            {
                var t = logistics.Find(n);
                if (t == null) throw new System.InvalidOperationException($"No {n} to move with {name}");
                attached.Add(t);
            }
            building.gameObject.AddComponent<StationSiteView>().EditorSetup(stationId, def.Footprint, attached, ProjectSetup.LoadSprite("Props/prop_scaffold"));
        }

        private static Transform Container(Transform parent, string id, string title, ContainerKind kind, Vector2 pos, string description,
            List<WorldContainerView.ResourceAmount> materials, string[] itemIds, int capacity = 0, bool hidden = false)
        {
            var root = new GameObject(title);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.AddComponent<SortingGroup>();
            if (kind == ContainerKind.OutputPad) Sprite(root.transform, "Pad", "Props/prop_output_pad", Vector2.zero);
            else if (!hidden) Sprite(root.transform, "Shadow", "Props/blob_shadow", new Vector2(0f, 0.05f)).transform.localScale = new Vector3(1.6f, 1.4f, 1f);

            var items = new List<ItemDefinition>();
            if (itemIds != null) foreach (var i in itemIds) items.Add(_catalogue.Get<ItemDefinition>(i));
            var view = root.AddComponent<WorldContainerView>();
            view.EditorSetup(id, kind, 0.45f, materials, items);
            view.EditorSetCapacity(capacity);

            var col = root.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.0f, 0.9f);
            col.offset = new Vector2(0f, 0.3f);
            if (!hidden) root.AddComponent<Inspectable>().EditorSetup(title, description, "");
            return root.transform;
        }

        /// <summary>
        /// Racks stand against the Store's back walls. Their sort point sits just in front of the Store's
        /// own pivot so they draw over the back walls, while the visual is lifted back into place.
        /// </summary>
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

        /// <summary>Phase 6: the visiting Trader and goods, shown only while the Trader is in town.</summary>
        private static void TraderStall(SpriteRenderer pad)
        {
            var generic = pad.GetComponent<Inspectable>();
            var title = generic.Title;
            Object.DestroyImmediate(generic);
            var stall = new GameObject("Stall");
            stall.transform.SetParent(pad.transform, false);
            stall.AddComponent<SortingGroup>();
            // The Trader is the citizen for now (2026-10-15), standing at the stall like the supplier stands.
            var trader0 = CitizenFigure.Create(stall.transform, "Trader", Vector2.zero, Citizen(),
                AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ProjectSetup.SupplierAnimationsPath));
            trader0.transform.localPosition = new Vector2(-0.45f, 0.1f);
            Sprite(stall.transform, "Goods", "Props/pile_lumber", new Vector2(0.45f, 0.05f), 0);
            Sprite(stall.transform, "Ingots", "Props/pile_ingots", new Vector2(0.15f, -0.2f), 1);
            var coin = Sprite(stall.transform, "Coin", "UI/Icons/ic_coin", new Vector2(-0.45f, 1.45f), 3);
            coin.transform.localScale = Vector3.one * 0.6f;
            stall.SetActive(false);
            var trader = pad.gameObject.AddComponent<TraderInspectable>();
            trader.EditorSetup(title, "A periodic visitor sets up here to sell external materials and buy Store goods at poor prices.", "");
            trader.EditorSetupTrader(stall);
        }

        private static List<WorldContainerView.ResourceAmount> Mats(params (string id, int qty)[] entries)
        {
            var list = new List<WorldContainerView.ResourceAmount>();
            foreach (var (id, qty) in entries)
                list.Add(new WorldContainerView.ResourceAmount { resource = _catalogue.Get<ResourceDefinition>(id), quantity = qty });
            return list;
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

        /// <summary>
        /// The horizon ahead (the journey, 2026-10-15): Astra's valley view as a band across the top of the screen, behind
        /// the land, with the castle on its far ridge: it shows where the ridge ends (Kevin: no fade, the land just ends at
        /// its edge; the castle needn't be in view all the time). Returns where adventurers come from and go to:
        /// a little way up the old road ahead, toward the castle.
        /// </summary>
        private static Vector2 Horizon(Transform world, SandboxSettings settings)
        {
            var par = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(PlaceholderSpriteImporter.ArtRoot + "View/parallax.json"));
            var layers = new List<ParallaxBackdrop.Layer>();
            var index = new Dictionary<string, int>();
            foreach (var l in par["layers"])
            {
                var file = ((string)l["file"]).Replace(".png", "");
                if (file == "view_front") continue;   // the dark treetops framed the old cliff
                index[file] = layers.Count;
                layers.Add(new ParallaxBackdrop.Layer
                {
                    sprite = ProjectSetup.LoadSprite("View/" + file),
                    top = (int)l["order"] == 0 ? ProjectSetup.LoadSprite("View/" + file + "_top") : null,
                    originPx = new Vector2((float)l["origin_px"][0], (float)l["origin_px"][1]),
                    factor = (float)l["parallax_factor"],
                    order = ParallaxBackdrop.BaseOrder + (int)l["order"] * 5,
                });
            }
            var backdrop = new GameObject("Horizon").AddComponent<ParallaxBackdrop>();
            backdrop.transform.SetParent(world, false);
            backdrop.EditorSetup(layers, 0.45f, Valley, _unlit);
            var c = par["castle"];
            backdrop.EditorSetupCastle(ProjectSetup.LoadSprite("View/castle"), index[((string)c["parent"])],
                new Vector2((float)c["ground_in_parent_px"][0], (float)c["ground_in_parent_px"][1]), (float)c["scale"] * 1.45f);   // the goal: bigger
            var w = par["walker"];
            var road = new List<Vector2>();
            foreach (var p in w["road_path_px"]) road.Add(new Vector2((float)p[0], (float)p[1]));
            backdrop.EditorSetupWalker(ProjectSetup.LoadSprite("View/valley_walker"), index[(string)w["parent"]], road);

            var ridge = settings.camp.Ridge;
            var (rx, ry) = ridge.PointAt(ridge.CampS + 26);
            return SandboxSettings.CellCentre(Mathf.RoundToInt((float)rx), Mathf.RoundToInt((float)ry));
        }

        private static HoseCharacterDefinition Citizen() =>
            AssetDatabase.LoadAssetAtPath<HoseCharacterDefinition>(ProjectSetup.CitizenDir + ProjectSetup.DefaultCitizenId + ".asset");

        /// <summary>The supplier's painted animations on a character's body sprite (the old overlay is retired).</summary>
        private static void Animate(Transform visual)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ProjectSetup.SupplierAnimationsPath);
            var idle = set != null ? set.Get("idle_S") ?? set.Get("idle_front") : null;
            if (idle == null || idle.frames.Length == 0) return;
            var body = visual.Find("Base").GetComponent<SpriteRenderer>();
            body.sprite = idle.frames[0];
            var overlay = visual.Find("Overlay");
            if (overlay != null) overlay.gameObject.SetActive(false);
            visual.gameObject.AddComponent<SupplierAnimator>().EditorSetup(set, body);
            // The rubber-hose supplier (2026-10-14): drawn live from painted parts; the frame body is kept hidden for
            // the animator's bookkeeping (clips, timing).
            var hose = AssetDatabase.LoadAssetAtPath<HoseCharacterDefinition>(ProjectSetup.StoreOwnerPath);
            if (hose != null)
            {
                visual.gameObject.AddComponent<HoseRig>().EditorSetup(hose);
                body.enabled = false;
            }
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
