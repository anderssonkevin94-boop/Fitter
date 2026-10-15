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
    /// Camp 1 (the journey, 2026-10-15): the town is the camp's clearing, wrapped by forest and hills on its two low sides
    /// and around its side tips, with the cliff along its two high sides. There is no quarry: the hills are the Mine, and
    /// the further out from the clearing, the tougher the trees and the deeper the ore.
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

        [UnityTest]
        public IEnumerator TheCampIsAClearingWrappedByForestWithTheCliffNorth()
        {
            yield return Boot();
            var settings = GameRoot.Instance.Config.SandboxSettings;
            var camp = settings.camp;
            Assert.NotNull(camp, "the scene shows a camp");
            Assert.AreEqual(camp, GameRoot.Instance.Config.Catalogue.CurrentCamp);
            var forest = Object.FindAnyObjectByType<ForestGridView>();
            var sim = S.Sites.Get("site.forest");
            var g = sim.Config.Grove;
            Assert.IsTrue(g.Ring, "the forest is the camp's ring");
            var c = camp.Clearing;
            var b = camp.Bounds;

            // Forest on both low sides and around the side tips; none in the clearing or past the cliff.
            Assert.IsTrue(forest.CellOf(c.xMin - 1, 0, out _), "forest beyond the clearing's lower-left edge");
            Assert.IsTrue(forest.CellOf(0, c.yMin - 1, out _), "forest beyond its lower-right edge");
            Assert.IsTrue(forest.CellOf(c.xMin - 3, c.yMax - 1, out _), "around the left tip, up to the cliff");
            Assert.IsTrue(forest.CellOf(c.xMax - 1, c.yMin - 3, out _), "around the right tip, up to the cliff");
            Assert.IsTrue(forest.CellOf(b.xMin, b.yMin, out _), "out to the camp's far corner");
            Assert.IsFalse(forest.CellOf(0, 0, out _), "the clearing is open");
            Assert.IsFalse(forest.CellOf(0, c.yMax, out _), "nothing past the cliff");
            Assert.IsNull(GameObject.Find("DigSpot"), "no quarry");
            Assert.IsNull(GameObject.Find("SurfaceOutcrops"), "no outcrops in the clearing");
            Assert.IsNull(GameObject.Find("Stone Face"), "no rock faces in the clearing");

            // The cliff lip runs the camp's whole width on both high sides.
            var lips = 0;
            foreach (Transform t in GameObject.Find("Overlook").transform) if (t.name == "CliffLip" || t.name == "CliffPath") lips++;
            Assert.GreaterOrEqual(lips, c.xMax - b.xMin + c.yMax - b.yMin);

            // Further out: tougher trees, and the hills hold deeper layers.
            var mine = S.Sites.Get("site.mine");
            Assert.AreEqual(1, sim.BandOf(c.xMin - 1, 0));
            Assert.Greater(sim.BandOf(c.xMin - 20, 0), 1, "the outer forest is a later band");
            Assert.Greater(sim.TreeDurabilityMicro(c.xMin - 20, 0), sim.TreeDurabilityMicro(c.xMin - 1, 0));
            Assert.Greater(mine.SurfaceBand(0, c.yMin - 20), mine.SurfaceBand(0, c.yMin - 1), "deeper ore further out");
            Assert.AreEqual(0, sim.TerrainHeight(c.xMin - 1, 0), "flat beside the clearing");

            // The view: stand at the clearing's left edge looking out over the forest.
            Motor.Teleport(SandboxSettings.CellCentre(c.xMin + 1, 2));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(1.0f);
            yield return Capture.Take("camp1_forest_edge");
            Motor.Teleport(SandboxSettings.CellCentre(c.xMin + 1, c.yMax - 3));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(1.0f);
            yield return Capture.Take("camp1_left_tip");
            Motor.Teleport(SandboxSettings.CellCentre(4, c.yMin + 1));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(1.0f);
            yield return Capture.Take("camp1_south");
            foreach (var x in new[] { c.xMin - 8, c.xMin - 18 })
            {
                Motor.Teleport(SandboxSettings.CellCentre(x, c.yMax - 2));
                SandboxSceneController.Current.SnapPlayerToSavedPosition();
                yield return new WaitForSeconds(1.0f);
                yield return Capture.Take($"camp1_cliff_{c.xMin - x}");
            }
        }

        [UnityTest]
        public IEnumerator TheForestHasHillsToClimbOrDigThrough()
        {
            yield return Boot();
            var forest = Object.FindAnyObjectByType<ForestGridView>();
            var sim = S.Sites.Get("site.forest");
            var g = sim.Config.Grove;
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
            Assert.Greater(forest.ShadowsDrawn, 0, "taller blocks cast shadows on the ground beside them");
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
        public IEnumerator TheForestIsAGridOfTreesAroundTheClearing()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var forest = Object.FindAnyObjectByType<ForestGridView>();
            var slab = MineSlabView.Current;
            var sim = s.Sites.Get("site.forest");
            var g = sim.Config.Grove;
            var ground = Map("Ground");
            Assert.NotNull(forest);
            Assert.IsTrue(sim.GroveMode, "any tree can be cut");
            var (ax, ay) = (g.ClearX0 - 1, 4);
            while (!sim.CanChop(ax, ay)) ay++;   // some spots are open ground
            Assert.IsTrue(forest.CellOf(ax, ay, out var firstCell));
            var first = ground.GetCellCenterWorld(firstCell);

            // Tightly grown: standing trees block the way; only cut tiles can be walked.
            Assert.IsTrue(forest.HasStandingTree(firstCell));
            Assert.IsFalse(slab.SurfaceWalkable(first), "a standing tree blocks the supplier");
            for (var i = 0; i < 4000 && !sim.IsFelled(ax, ay); i++) s.Sites.PlayerChopCell("site.forest", ax, ay, 0.5f);
            yield return null;
            Assert.IsFalse(forest.HasStandingTree(firstCell), "a felled tree leaves open ground");
            Assert.IsTrue(slab.SurfaceWalkable(first));

            // Further out within a band costs a little more; the next band (further out) more again.
            Assert.Greater(sim.TreeDurabilityMicro(ax - 5, ay), sim.TreeDurabilityMicro(ax, ay));
            Assert.Greater(sim.TreeDurabilityMicro(ax - g.BandWidth, ay), sim.TreeDurabilityMicro(ax, ay));
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
        }
    }
}
