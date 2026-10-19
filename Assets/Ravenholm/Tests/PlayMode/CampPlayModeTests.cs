using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Ravenholm.Domain.Sites;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Camp 1 (the journey, Kevin 2026-10-15): the camp's clearing sits on a long, wide, winding mountain top that leads
    /// forward (up the screen) toward the castle on the horizon, past a gate of old trees. There is no quarry: the hills
    /// are the Mine, and the further forward, the tougher the trees and the deeper the ore.
    /// </summary>
    public sealed class CampPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-camp", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameRoot.Instance != null && GameRoot.Instance.InputRouter != null)
            {
                GameRoot.Instance.InputRouter.ScriptedMove = null;
                GameRoot.Instance.InputRouter.ScriptedPress = null;
            }
            if (GameRoot.Instance != null && GameRoot.Instance.Hud != null) GameRoot.Instance.Hud.ScriptedHold = false;
            if (GameRoot.Instance != null) Object.Destroy(GameRoot.Instance.gameObject);
            yield return null;
            yield return null;
            GameRoot.SaveDirectoryOverride = null;
            try { Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        private static IEnumerator Boot()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            yield return null;
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.NotNull(SandboxSceneController.Current);
            for (var i = 0; i < 3; i++) yield return null;
        }

        private static PlayerMotor Motor => SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
        private static Ravenholm.Services.GameServices S => GameRoot.Instance.Services;
        private static Tilemap Map(string name) => GameObject.Find(name)?.GetComponent<Tilemap>();
        private static void Stick(Vector2? v) => GameRoot.Instance.InputRouter.ScriptedMove = v;

        // The stick's diagonals walk along the grid's axes: +x, +y, -x, -y.
        private static readonly Vector3Int[] Axes = { new Vector3Int(1, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, -1, 0) };
        private static Vector2 StickFor(Vector3Int d) => new Vector2(d.x - d.y, d.x + d.y).normalized;
        private static Vector2 ScreenStep(Vector3Int d) => new Vector2((d.x - d.y) * 0.5f, (d.x + d.y) * 0.25f);

        private static IEnumerator Until(Func<bool> done, float seconds, string what) => Until(done, seconds, () => what);

        private static IEnumerator Until(Func<bool> done, float seconds, Func<string> what)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(done(), what());
        }

        /// <summary>The first tree that can be cut on or beside the line, at least <paramref name="ahead"/> cells forward of the camp.</summary>
        /// <summary>
        /// The furthest tree of this level at most <paramref name="before"/> cells forward (searching back toward the camp):
        /// the world seed differs every new game, and the narrow pass before a gate is sometimes bare.
        /// </summary>
        public static Vector3Int FarTree(SiteSimulator sim, double before)
        {
            var r = sim.Config.Grove.Ridge;
            for (var s = before; s >= 20; s -= 1)
            for (var k = 0; k <= 12; k++)
            foreach (var side in new[] { k, -k })
            {
                var (bx, by) = r.PointBeside(r.CampS + s, side);
                var (x, y) = ((int)System.Math.Round(bx), (int)System.Math.Round(by));
                if (sim.CanChop(x, y) && r.GateTierAt(x, y) == 0) return new Vector3Int(x, y, 0);
            }
            Assert.Fail($"no tree before {before}");
            return default;
        }

        public static Vector3Int TreeAhead(SiteSimulator sim, double ahead, bool anyTier = false, bool withNeighbour = false)
        {
            var r = sim.Config.Grove.Ridge;
            for (var s = ahead; s < r.Length - r.CampS; s += 1)
            {
                for (var k = 0; k <= 12; k++)
                foreach (var side in new[] { k, -k })
                {
                    var (bx, by) = r.PointBeside(r.CampS + s, side);   // across the road, whichever way it runs
                    var (x, y) = ((int)System.Math.Round(bx), (int)System.Math.Round(by));
                    if (!sim.CanChop(x, y) || !anyTier && r.GateTierAt(x, y) != 0) continue;
                    // On flat ground with another flat tree right beside it (the trees are spaced out: not every one has).
                    if (withNeighbour && (sim.TerrainHeight(x, y) != 0 ||
                                          !(Flat(x + 1, y) || Flat(x - 1, y) || Flat(x, y + 1) || Flat(x, y - 1)))) continue;
                    return new Vector3Int(x, y, 0);
                }
            }
            Assert.Fail($"no tree {ahead} ahead");
            bool Flat(int x, int y) => sim.CanChop(x, y) && sim.TerrainHeight(x, y) == 0 && r.GateTierAt(x, y) == 0;
            return default;
        }

        private static IEnumerator Look(Vector2 at, string capture)
        {
            Motor.Teleport(at);
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(1.0f);
            yield return Capture.Take(capture);
        }

        [UnityTest]
        public IEnumerator TheCampSitsOnAWindingRidgeThatLeadsForward()
        {
            yield return Boot();
            GameRoot.Instance.Hud.Tips.HideAll();   // a clear view for the pictures
            var settings = GameRoot.Instance.Config.SandboxSettings;
            var camp = settings.camp;
            Assert.NotNull(camp, "the scene shows a camp");
            Assert.AreEqual(camp, GameRoot.Instance.Config.Catalogue.CurrentCamp);
            var forest = Object.FindAnyObjectByType<ForestGridView>();
            var sim = S.Sites.Get("site.forest");
            var r = sim.Config.Grove.Ridge;
            Assert.NotNull(r, "the forest is the camp's ridge");
            var ground = Map("Ground");

            // The clearing is open; the forest is ahead; nothing past the ragged edges.
            Assert.IsFalse(forest.CellOf((int)camp.ClearingCentre.x, (int)camp.ClearingCentre.y, out _), "the clearing is open");
            var ahead = TreeAhead(sim, 20);
            Assert.IsTrue(forest.CellOf(ahead.x, ahead.y, out _), "trees ahead of the camp");
            var (px, py) = r.PointAt(r.CampS + 60);
            Assert.IsFalse(ground.HasTile(new Vector3Int((int)px + 70, (int)py - 70, 0)), "no land far off to the side");
            Assert.IsNull(GameObject.Find("DigSpot"), "no quarry");
            Assert.IsNull(GameObject.Find("Overlook"), "no cliff overlook");
            Assert.IsNotNull(GameObject.Find("Horizon"), "the horizon ahead");

            // Further forward: tougher trees, and the hills hold deeper layers.
            var mine = S.Sites.Get("site.mine");
            // Gated levels (2026-10-16): one band per level, tougher toward its gate; the next band is the next level's.
            var far = camp.Plateau ? FarTree(sim, camp.GateAhead(0) - 2) : TreeAhead(sim, 100);
            Assert.AreEqual(1, sim.BandOf(ahead.x, ahead.y));
            if (!camp.Plateau) Assert.Greater(sim.BandOf(far.x, far.y), 1, "a later band further forward");
            Assert.Greater(sim.TreeDurabilityMicro(far.x, far.y), sim.TreeDurabilityMicro(ahead.x, ahead.y));
            if (!camp.Plateau) Assert.Greater(mine.SurfaceBand(far.x, far.y), mine.SurfaceBand(ahead.x, ahead.y), "deeper ore further forward");

            // The gate (the horseshoe levels, 2026-10-19): a project in the north-west pass, between ridges no tool breaks.
            var gateAhead = camp.GateAhead(0);
            var (ridgeX, ridgeY) = r.PointBeside(r.Gates[0].S, 5);
            var rx = (int)System.Math.Round(ridgeX); var ry = (int)System.Math.Round(ridgeY);
            Assert.IsTrue(r.InPassRidge(rx, ry), "a ridge beside the pass");
            Assert.IsTrue(S.Sites.ToolTooWeak(rx, ry), "no tool breaks it");

            // Pictures for Kevin: the camp, the road ahead, the gate, the next clearing.
            yield return Look(Vector2.zero, "journey_camp");
            foreach (var at in new[] { 22, 38, gateAhead - 6, camp.NextCampAhead })
            {
                var (x, y) = r.PointAt(r.CampS + at);
                yield return Look(SandboxSettings.CellCentre((int)x, (int)y), $"journey_ahead_{at}");
            }

            // At the ridge's edge: a short face of earth, then the valley; and never near Unity's renderer limit (4095):
            // the long ragged edges once drew a face down the whole screen at each of them (21,000 sprites, 2026-10-15).
            var (ex, ey) = r.PointAt(r.CampS + 38);
            var edge = new Vector3Int((int)ex, (int)ey, 0);
            while (ground.HasTile(edge + new Vector3Int(1, -1, 0))) edge += new Vector3Int(1, -1, 0);
            yield return Look(SandboxSettings.CellCentre(edge.x, edge.y), "journey_edge");
            var drawn = 0;
            foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) if (sr.enabled) drawn++;
            Assert.Less(drawn, 3000, $"sprites drawn at the edge ({drawn})");
        }

        [UnityTest]
        public IEnumerator TheForestHasHillsToClimbOrDigThrough()
        {
            yield return Boot();
            var forest = Object.FindAnyObjectByType<ForestGridView>();
            var sim = S.Sites.Get("site.forest");
            var ground = Map("Ground");
            int H(Vector3Int c) => sim.TerrainHeight(c.x, c.y);
            bool Laid(Vector3Int c) => forest.CellOf(c.x, c.y, out _);
            void ClearCell(Vector3Int c)
            {
                if (!Laid(c)) return;
                for (var i = 0; i < 4000 && sim.CanChop(c.x, c.y); i++) S.Sites.PlayerChopCell("site.forest", c.x, c.y, 0.5f);
                S.Sites.ClearStump("site.forest", c.x, c.y);
                S.Logistics.System.DebugUnloadCarry();
            }
            // Find a slope (floor, then one block up, maybe two), and a wall (two or more blocks up) to dig, in the ring.
            Vector3Int slope = default, slopeDir = default, wall = default, wallDir = default;
            int slopeTop = 0;
            bool foundSlope = false, foundWall = false;
            var cb = GameRoot.Instance.Config.SandboxSettings.camp.Bounds;
            for (var x = cb.xMin + 2; x < cb.xMax - 2 && !(foundSlope && foundWall); x++)
            for (var y = cb.yMin + 2; y < cb.yMax - 2 && !(foundSlope && foundWall); y++)
            {
                var c = new Vector3Int(x, y, 0);
                if (!Laid(c)) continue;
                foreach (var d in Axes)
                {
                    if (!Laid(c + d * 3) || !Laid(c - d)) continue;
                    if (!foundSlope && H(c) == 0 && H(c + d) == 1)
                    {
                        foundSlope = true; slope = c; slopeDir = d;
                        slopeTop = H(c + d * 2) == 2 ? 2 : 1;
                    }
                    if (!foundWall && H(c) <= 2 && H(c + d) - H(c) >= 2) { foundWall = true; wall = c; wallDir = d; }
                }
            }
            Assert.IsTrue(foundSlope, "the hills have a gentle slope somewhere");
            Assert.IsTrue(foundWall, "and a steep face somewhere");

            S.Sites.SpeedMultiplier = 50f;
            var side = new Vector3Int(slopeDir.y, slopeDir.x, 0);
            for (var k = -1; k <= 3; k++)
            for (var s = -1; s <= 1; s++) ClearCell(slope + slopeDir * k + side * s);   // a corridor: the body is wide
            // Trees in reach win the button over rock (by design): clear the trees around the wall.
            for (var dx = -2; dx <= 2; dx++)
            for (var dy = -2; dy <= 2; dy++) ClearCell(wall + new Vector3Int(dx, dy, 0));
            S.Sites.SpeedMultiplier = 1f;

            // Walk up the slope: one block a step.
            Motor.Teleport(ground.GetCellCenterWorld(slope));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(0, forest.PlayerFeet, "on the forest floor");
            Stick(StickFor(slopeDir));
            yield return Until(() => forest.PlayerFeet >= 1, 10f, () => $"climbs the slope (feet {forest.PlayerFeet}, at {Motor.transform.position})");
            Stick(null);
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(Motor.Elevation, 0.2f, "drawn up on the blocks");
            yield return Capture.Take("forest_pass");

            // A steep face (two or more blocks up): too high to step onto; dig into it, head height first.
            var behind = wall + wallDir;
            Motor.Teleport(ground.GetCellCenterWorld(wall));
            yield return new WaitForSeconds(0.3f);
            Assert.GreaterOrEqual(forest.HeightAt(behind) - forest.PlayerFeet, 2, "a wall");
            Assert.IsFalse(forest.CanEnter(ground.GetCellCenterWorld(wall), ground.GetCellCenterWorld(behind)), "too high to step onto");
            yield return Until(() => forest.ShadowsDrawn > 0, 2f, () => "taller blocks cast shadows on the ground beside them");
            var feet = forest.PlayerFeet;
            var spot = Object.FindAnyObjectByType<ForestRockSpot>();
            // Walking into the wall stops the feet short of it: the wide body does not sink into the rock.
            Stick(StickFor(wallDir));
            yield return new WaitForSeconds(0.8f);
            Stick(null);
            var at = (Vector2)Motor.transform.position;
            Assert.AreEqual(wall, ground.WorldToCell(at), "still in front of the wall");
            Assert.AreEqual(wall, ground.WorldToCell(at + ScreenStep(wallDir) * (PlayerMotor.BodyClearance * 0.9f)), $"the feet keep clear of the wall (at {at})");
            GameRoot.Instance.InputRouter.ScriptedMove = StickFor(wallDir) * 0.3f;   // face into the wall (it doesn't give)
            yield return null;
            GameRoot.Instance.InputRouter.ScriptedMove = null;
            yield return Until(() => SandboxSceneController.Current.ContextTarget is ForestRockSpot && forest.HighlightShown, 2f,
                () => $"the rock about to be dug is highlighted (target {SandboxSceneController.Current.ContextTarget})");
            yield return Capture.Take("forest_rock_highlight");
            var carry = S.Logistics.System.CarryCount;
            S.Sites.SpeedMultiplier = 20f;
            for (var i = 0; i < 600 && (forest.SolidAt(behind, feet + 1) || forest.SolidAt(behind, feet + 2)); i++)
            {
                yield return null;
                if (spot.Available) spot.OnHold(Time.deltaTime);
                S.Logistics.System.DebugUnloadCarry();
            }
            S.Sites.SpeedMultiplier = 1f;
            Assert.IsFalse(forest.SolidAt(behind, feet + 1) || forest.SolidAt(behind, feet + 2), "dug a way into the rock");
            CollectionAssert.IsEmpty(S.Logistics.System.Audit());
        }

        [UnityTest]
        public IEnumerator TheForestIsAGridOfTreesAheadOfTheCamp()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var forest = Object.FindAnyObjectByType<ForestGridView>();
            var slab = MineSlabView.Current;
            var sim = s.Sites.Get("site.forest");
            var ground = Map("Ground");
            Assert.NotNull(forest);
            Assert.IsTrue(sim.GroveMode, "any tree can be cut");
            var t0 = TreeAhead(sim, 20, withNeighbour: true);   // on flat ground with a tree beside it (hills are everywhere now)
            var (ax, ay) = (t0.x, t0.y);
            Assert.IsTrue(forest.CellOf(ax, ay, out var firstCell));
            var first = ground.GetCellCenterWorld(firstCell);

            // Tightly grown: standing trees block the way; only cut tiles can be walked.
            Assert.IsTrue(forest.HasStandingTree(firstCell));
            Assert.IsFalse(slab.SurfaceWalkable(first), "a standing tree blocks the supplier");
            for (var i = 0; i < 4000 && !sim.IsFelled(ax, ay); i++) s.Sites.PlayerChopCell("site.forest", ax, ay, 0.5f);
            yield return null;
            Assert.IsFalse(forest.HasStandingTree(firstCell), "a felled tree leaves open ground");
            Assert.IsTrue(slab.SurfaceWalkable(first));

            // Further forward costs more: the next band (beyond the gate) more again.
            var road = GameRoot.Instance.Services.Catalogue.CurrentCamp;
            var t1 = road.Plateau ? FarTree(sim, road.GateAhead(0) - 2) : TreeAhead(sim, 100);
            Assert.Greater(sim.TreeDurabilityMicro(t1.x, t1.y), Ravenholm.Domain.Sites.SiteMath.Durability(sim.Config, 1, 0) * 1000,
                road.Plateau ? "toward the gate, tougher than the first" : "band 2's trees are tougher than band 1's first");
            Motor.Teleport(first);   // on the stump, among the standing trees
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(1.5f);
            foreach (var tree in forest.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!tree.name.StartsWith("Tree") && tree.name != "ForestTree") continue;
                Assert.AreEqual("Ground", tree.sortingLayerName, "trees sort among the Forest's blocks, by cell and level");
                Assert.Greater(tree.sortingOrder, ForestGridView.TerrainBase - 2000);
            }
            yield return Capture.Take("forest_ring");

            // The tree about to be chopped is highlighted as a block is (2026-10-16, Kevin).
            var chop = SandboxSceneController.Current.ContextTarget as ForestChopSpot;
            Assert.NotNull(chop, "a tree in reach is the button's target");
            Assert.IsTrue(forest.TreeHighlightShown, "and it is highlighted");
            yield return Capture.Take("forest_tree_highlight");

            // The ground draws as one batched chunk, not one sorted item per tile (Unity's 4,095 limit: the red text).
            var groundRenderer = Map("Ground").GetComponent<UnityEngine.Tilemaps.TilemapRenderer>();
            Assert.AreEqual(UnityEngine.Tilemaps.TilemapRenderer.Mode.Chunk, groundRenderer.mode);
        }
    }
}
