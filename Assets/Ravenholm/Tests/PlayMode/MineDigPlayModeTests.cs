using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// The Mine as a dig slab (GDD 6/7, 2026-10-12): the supplier digs into the ground at the Mine's corner, two blocks
    /// tall, tunnels along the cliff, picks blocks by walking into them or tapping them, climbs out on stairs they dig,
    /// and leaves a routine carrying on their last tunnel. The frontier Forest grows over the slab.
    /// </summary>
    public sealed class MineDigPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-dig", Guid.NewGuid().ToString("N"));
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
        private static SiteSimulator Mine => S.Sites.Get("site.mine");
        private static Tilemap Map(string name) => GameObject.Find(name)?.GetComponent<Tilemap>();
        private static void Stick(Vector2? v) => GameRoot.Instance.InputRouter.ScriptedMove = v;
        private static void HoldDig(bool held) => GameRoot.Instance.Hud.ScriptedHold = held;

        /// <summary>Screen direction of walking along the cliff (+u).</summary>
        // The stick's diagonals walk along the grid's axes (up or down the screen moves at half speed: the dwarf's painted walk).
        private static readonly Vector2 Along = new Vector2(-1f, 1f).normalized;
        private static readonly Vector2 Into = new Vector2(1f, 1f).normalized;

        private static IEnumerator Until(Func<bool> done, float seconds, string what) => Until(done, seconds, () => what);

        private static IEnumerator Until(Func<bool> done, float seconds, Func<string> what)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(done(), what());
        }

        /// <summary>Taps the screen over a block until it is the selection (overlapping blocks take turns).</summary>
        private static void TapUntilSelected(MineSlabView slab, DigCell b, Vector3 offset)
        {
            var spot = Camera.main.WorldToScreenPoint(slab.Top(b.X, b.U, b.Row) + offset);
            for (var tap = 0; tap < 6 && !Equals(slab.Selected, b); tap++) slab.OnTapped(spot);
            Assert.AreEqual(b, slab.Selected, $"tapping selects {b.X},{b.U},{b.Row} (reachable {slab.Reachable().Count}, shown {slab.Shown(b.X, b.U, b.Row)}, " +
                                              $"diggable {Mine.CanDig(b.X, b.U, b.Row)}, near {slab.NearestReachable(slab.Top(b.X, b.U, b.Row) + offset).Count}, " +
                                              $"player {SandboxSceneController.Current.Player.position}, aim {slab.Top(b.X, b.U, b.Row)}, spot {spot})");
        }

        /// <summary>Into the Mine through its entry: tap the block underfoot and hold Dig, down to <paramref name="feet"/>.</summary>
        private static IEnumerator DigDown(MineSlabView slab, int feet)
        {
            Motor.Teleport(slab.Top(0f, 1f, 0f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(1.0f);   // camera smoothing is real-time
            while (slab.Feet < feet || !slab.PlayerInside)
            {
                yield return Until(() => !slab.PlayerInside || Mine.Solid(0, 1, slab.Feet), 3f, "lands on the floor of the hole");
                var before = slab.PlayerInside ? slab.Feet : -1;
                var under = new DigCell(0, 1, slab.PlayerInside ? slab.Feet : 0);
                yield return null;
                TapUntilSelected(slab, under, new Vector3(0f, -0.3f, 0f));
                yield return null;
                Assert.IsInstanceOf<MineDigSpot>(SandboxSceneController.Current.ContextTarget, "the context button offers Dig");
                HoldDig(true);
                yield return Until(() => Mine.IsDug(under.X, under.U, under.Row), 10f, "digs the block underfoot");
                HoldDig(false);   // let go as it breaks (held on, Dig carries on into the wall ahead)
                yield return Until(() => slab.PlayerInside && slab.Feet > before, 3f, "and drops into it");
            }
        }

        // ------------------------------------------------------------------ the dig slab

        [UnityTest]
        public IEnumerator DigAQuarryIntoTheGroundAndClimbOutOnSteps()
        {
            yield return Boot();
            var slab = MineSlabView.Current;
            Assert.NotNull(slab, "the Mine is a dig slab");
            Assert.IsTrue(Mine.DigMode);
            // Only what the camera sees is drawn: stand by the quarry first.
            Motor.Teleport(slab.Top(0f, 1f, 0f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(slab.DrawnBlocks, 0, "the town's cliff edges are drawn as blocks");
            S.Sites.SpeedMultiplier = 20f;

            // The quarry's plot: its whole surface can be broken into; the ground around it can't.
            var q = Mine.Slab;
            Assert.IsTrue(Mine.CanDig(0, 1, 0) && Mine.CanDig(q.Thickness - 1, q.MinU, 0), "anywhere on the plot");
            Assert.IsFalse(Mine.CanDig(q.Thickness, 1, 0) || Mine.CanDig(0, q.Length, 0), "not past it");
            yield return DigDown(slab, 3);
            Assert.AreEqual(3, slab.Feet, "three blocks down: room to stand, two blocks tall");
            Assert.Greater(S.Logistics.System.CarryCount, 0, "dug ore goes straight into the carry");
            Assert.AreEqual(0, slab.StrongFlecks, "ore isn't shown in the rock around the supplier (no lamp reveal)");
            Assert.IsTrue(Mine.Record.Revealed.ContainsKey(1), "the first layer is revealed by digging into it");

            // Into the plot (back from the cliff): walking into the wall with Dig held digs head height, then legs, then steps in.
            Stick(Into);
            HoldDig(true);
            yield return Until(() => slab.X > 2.5f, 40f, () => $"digs back into the plot and walks in (x {slab.X}, feet {slab.Feet}, target {slab.Target})");
            HoldDig(false);
            Stick(null);
            Assert.AreEqual(3, slab.Feet, "level: no falling, no climbing");
            var lane = Mathf.RoundToInt(slab.X);
            var col = Mathf.RoundToInt(slab.U);
            Assert.IsTrue(Mine.IsDug(lane, col, 2) && Mine.IsDug(lane, col, 1), "standing in a two-high cut");
            Assert.IsNotNull(Mine.Record.LastDig, "remembered for routines");
            yield return null;
            {
                var f = slab.Facing;
                var head = new DigCell(lane + f.x, col + f.y, slab.Feet - 2);
                var legs = new DigCell(lane + f.x, col + f.y, slab.Feet - 1);
                var under = new DigCell(lane, col, slab.Feet);
                var expected = Mine.CanDig(head.X, head.U, head.Row) ? head : Mine.CanDig(legs.X, legs.U, legs.Row) ? legs : under;
                Assert.IsNull(slab.Selected);
                Assert.AreEqual(expected, slab.Target, "head height first, then legs, else the block underfoot");
            }
            yield return null;
            Assert.Less(slab.DrawnOrder(lane, col, slab.Feet), slab.PlayerOrder, "the floor block is behind the supplier");
            Assert.Greater(slab.FadedBlocks, 0, "down in the pit, the rock in front turns see-through");

            // Reachable blocks wear a faint outline; pressing and holding on one digs it without the button.
            Assert.Greater(slab.OutlinedBlocks, 0);
            var floorAhead = new DigCell(lane, col + 1, slab.Feet);
            var wallBeside = new DigCell(lane, col + 1, slab.Feet - 1);   // the wall beside, at leg height (in view)
            Assert.IsTrue(slab.Reachable().Contains(wallBeside), "the wall beside is reachable");
            GameRoot.Instance.InputRouter.ScriptedPress = Camera.main.WorldToScreenPoint(slab.Top(wallBeside.X, wallBeside.U, wallBeside.Row) + new Vector3(0.15f, -0.35f, 0f));
            yield return Until(() => Mine.IsDug(wallBeside.X, wallBeside.U, wallBeside.Row), 10f, "press and hold digs the block under the finger");
            GameRoot.Instance.InputRouter.ScriptedPress = null;

            // The Tap button rings for ore nearby; cracks show while a block is dug.
            var rings = slab.Tap();
            Assert.Greater(slab.TapCooldownLeft, 0f);
            Assert.AreEqual(rings, slab.LastTapRings);
            S.Sites.SpeedMultiplier = 1f;
            HoldDig(true);
            yield return Until(() => slab.CrackShowing, 5f, "the block cracks as it is dug");
            HoldDig(false);
            S.Sites.SpeedMultiplier = 20f;

            // Build: a scaffold over a hole beside (one lumber), stood on like rock. First open the way to it and the hole.
            foreach (var row in new[] { floorAhead.Row - 1, floorAhead.Row - 2, floorAhead.Row })
                for (var i = 0; i < 4000 && !Mine.IsDug(lane, floorAhead.U, row); i++) S.Sites.PlayerDig(lane, floorAhead.U, row, 1f, null);
            S.Logistics.System.ProduceShared("resource.cedar_lumber", 3, "test");
            slab.BuildMode = true;
            yield return null;
            Assert.IsTrue(slab.Buildable().Contains(floorAhead), "the hole beside can take a scaffold");
            var lumber = S.Sites.ScaffoldMaterialInStock("site.mine");
            slab.BuildAt(slab.Top(floorAhead.X, floorAhead.U, floorAhead.Row) + new Vector3(0f, -0.25f, 0f));
            Assert.IsTrue(Mine.IsScaffold(floorAhead.X, floorAhead.U, floorAhead.Row), "a scaffold is built there");
            Assert.AreEqual(lumber - 1, S.Sites.ScaffoldMaterialInStock("site.mine"), "for one lumber");
            slab.BuildMode = false;
            yield return null;
            Assert.AreEqual(1, slab.ScaffoldsDrawn);
            var feetBefore = slab.Feet;
            Stick(Along);
            yield return Until(() => Mathf.RoundToInt(slab.U) == floorAhead.U, 5f, "walks onto the scaffold");
            Stick(null);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(feetBefore, slab.Feet, "standing on the scaffold, not fallen into the hole");
            yield return new WaitForSeconds(0.3f);
            yield return Capture.Take("mine_dig_slab");

            // Out of the pit on steps: back to the first hole (open to the sky), then up steps cut toward the town.
            Stick(-Along);
            yield return Until(() => Mathf.RoundToInt(slab.U) == 1, 10f, "steps back off the scaffold into the cut");
            Stick(-Into);
            yield return Until(() => Mathf.RoundToInt(slab.X) == 0, 10f, () => $"walks back to the first hole (x {slab.X}, u {slab.U}, feet {slab.Feet})");
            Stick(null);
            void Carve(int x, int u, int row) { for (var i = 0; i < 4000 && !Mine.IsDug(x, u, row); i++) S.Sites.PlayerDig(x, u, row, 1f, null); }
            Carve(0, 0, 0); Carve(0, 0, 1);   // a step to feet 2
            Carve(0, -1, 0);                  // a step to feet 1
            Stick(-Along);
            yield return Until(() => !slab.PlayerInside, 10f, () => $"climbs the steps out of the pit (u {slab.U}, feet {slab.Feet})");
            Stick(null);
            var at = (Vector2)SandboxSceneController.Current.Player.position;
            Assert.IsTrue(slab.SurfaceWalkable(at), $"back on the ground (at {at})");
            S.Sites.SpeedMultiplier = 1f;
        }

        [UnityTest]
        public IEnumerator TheForestHasHillsToClimbOrDigThrough()
        {
            yield return Boot();
            var forest = Object.FindAnyObjectByType<ForestGridView>();
            var sim = S.Sites.Get("site.forest");
            var g = sim.Grove;
            var ground = Map("Ground");
            Vector3Int CellAt(int back, int along)
            {
                Assert.IsTrue(forest.CellOf(along / g.AreaLength + 1, back * g.AreaLength + along % g.AreaLength, out var c));
                return c;
            }
            int Slot(int back, int along) => back * g.AreaLength + along % g.AreaLength;
            int Area(int along) => along / g.AreaLength + 1;
            int H(int back, int along) => sim.TerrainHeight(Area(along), Slot(back, along));
            void ClearCell(int back, int along)
            {
                var (a, sl) = (Area(along), Slot(back, along));
                for (var i = 0; i < 4000 && sim.CanChop(a, sl); i++) S.Sites.PlayerChopCell("site.forest", a, sl, 0.5f);
                S.Sites.ClearStump("site.forest", a, sl);
                S.Logistics.System.DebugUnloadCarry();
            }
            // Find a natural slope going back from the cliff (0, 1, 2 blocks), then a wall (two or more blocks up) to dig.
            int slope = -1, slopeBack = 0, slopeTop = 0, wallBack = -1, wallAlong = -1;
            bool Laid(int back, int along) => forest.CellOf(Area(along), Slot(back, along), out _);   // the part laid out in town
            // (the world seed differs between runs: search the laid-out forest for floor, then one block, ideally two)
            for (var along = g.FlatEntrance + 1; along < 200 && slope < 0; along++)
            for (var back = 0; back < 16 && slope < 0; back++)
                if (Laid(back + 3, along) && H(back, along) == 0 && H(back + 1, along) == 1)
                {
                    slope = along; slopeBack = back;
                    slopeTop = H(back + 2, along) == 2 ? 2 : 1;
                }
            for (var along = g.FlatEntrance + 1; along < 200 && wallAlong < 0; along++)
            for (var back = 1; back < 20 && wallAlong < 0; back++)
                if (Laid(back + 1, along) && H(back, along) <= 2 && H(back + 1, along) - H(back, along) >= 2) { wallBack = back; wallAlong = along; }
            Assert.GreaterOrEqual(slope, 0, "the hills have a gentle slope somewhere");
            Assert.GreaterOrEqual(wallAlong, 0, "and a steep face somewhere");

            S.Sites.SpeedMultiplier = 50f;
            for (var back = slopeBack; back <= slopeBack + 3; back++)
            for (var along = slope - 1; along <= slope + 1; along++) ClearCell(back, along);   // a corridor: the body is wide
            // Trees in reach win the button over rock (by design): clear the trees around the wall.
            for (var back = Mathf.Max(0, wallBack - 2); back <= wallBack + 2; back++)
            for (var along = wallAlong - 2; along <= wallAlong + 2; along++) ClearCell(back, along);
            S.Sites.SpeedMultiplier = 1f;

            // Walk back from the cliff up the slope: one block a step.
            Motor.Teleport(ground.GetCellCenterWorld(CellAt(slopeBack, slope)));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(0, forest.PlayerFeet, "on the forest floor");
            Stick(Into);
            yield return Until(() => forest.PlayerFeet == slopeTop, 10f, () => $"climbs the slope (feet {forest.PlayerFeet}, at {Motor.transform.position})");
            Stick(null);
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(Motor.Elevation, 0.2f, "drawn up on the blocks");
            yield return Capture.Take("forest_pass");

            // A steep face (two or more blocks up): too high to step onto; dig into it, head height first.
            var rockCell = CellAt(wallBack, wallAlong);
            var behind = CellAt(wallBack + 1, wallAlong);
            Motor.Teleport(ground.GetCellCenterWorld(rockCell));
            yield return new WaitForSeconds(0.3f);
            Assert.GreaterOrEqual(forest.HeightAt(behind) - forest.PlayerFeet, 2, "a wall");
            Assert.IsFalse(forest.CanEnter(ground.GetCellCenterWorld(rockCell), ground.GetCellCenterWorld(behind)), "too high to step onto");
            var shadowsBefore = forest.ShadowsDrawn;
            Assert.Greater(shadowsBefore, 0, "taller blocks cast shadows on the ground beside them");
            // Dig the two blocks at body height out of the wall through the rock spot, standing in front of it.
            var feet = forest.PlayerFeet;
            var spot = Object.FindAnyObjectByType<ForestRockSpot>();
            // Walking into the wall stops the feet short of it: the wide body does not sink into the rock.
            var before = (Vector2)Motor.transform.position;
            Stick(Into);
            yield return new WaitForSeconds(0.8f);
            Stick(null);
            var at = (Vector2)Motor.transform.position;
            Assert.AreEqual(rockCell, ground.WorldToCell(at), "still in front of the wall");
            Assert.AreEqual(rockCell, ground.WorldToCell(at + new Vector2(0.5f, 0.25f) * (PlayerMotor.BodyClearance * 0.9f)), $"the feet keep clear of the wall (at {at}, from {before})");
            yield return Capture.Take("forest_wall_clearance");
            GameRoot.Instance.InputRouter.ScriptedMove = Into * 0.3f;   // face into the wall (it doesn't give)
            yield return null;
            GameRoot.Instance.InputRouter.ScriptedMove = null;
            yield return Until(() => SandboxSceneController.Current.ContextTarget is ForestRockSpot && forest.HighlightShown, 2f,
                () => $"the rock about to be dug is highlighted (target {SandboxSceneController.Current.ContextTarget})");
            yield return Capture.Take("forest_rock_highlight");
            S.Sites.SpeedMultiplier = 20f;
            for (var i = 0; i < 600 && (forest.SolidAt(behind, feet + 1) || forest.SolidAt(behind, feet + 2)); i++)
            {
                yield return null;
                if (spot.Available) spot.OnHold(Time.deltaTime);
            }
            S.Sites.SpeedMultiplier = 1f;
            Assert.IsFalse(forest.SolidAt(behind, feet + 1) || forest.SolidAt(behind, feet + 2), "dug a way into the rock");
            if (forest.HeightAt(behind) > feet + 2)
            {
                yield return new WaitForSeconds(0.2f);
                Assert.Greater(forest.ShadowsDrawn, 0, "the rock left overhead shades the floor under it");
            }
            CollectionAssert.IsEmpty(S.Logistics.System.Audit());
        }

        [UnityTest]
        public IEnumerator SurfaceOutcropsAreMinedAboveGround()
        {
            yield return Boot();
            var view = SurfaceOutcropView.Current;
            Assert.NotNull(view, "rock outcrops stand on the town's surface");
            Assert.GreaterOrEqual(view.Columns.Count, 14, "two groups of 7–10");
            var tall = view.Columns.First(c => c.z == 3);
            Assert.IsTrue(view.Columns.All(c => c.z >= 1 && c.z <= 3), "up to three blocks high");
            var cell = new Vector3Int(tall.x, tall.y, 0);
            var ground = Map("Ground");
            Assert.IsTrue(view.Blocked(cell));
            Assert.IsFalse(MineSlabView.Current.SurfaceWalkable(ground.GetCellCenterWorld(cell)), "rock can't be walked through");

            // Stand beside the tall column and hold the main button: the top block breaks first, its ore into the carry.
            Motor.Teleport(ground.GetCellCenterWorld(cell) + new Vector3(0.75f, -0.3f, 0f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.6f);
            var spot = SandboxSceneController.Current.ContextTarget as OutcropSpot;
            Assert.NotNull(spot, $"the main button offers Mine (target {SandboxSceneController.Current.ContextTarget})");
            Assert.AreEqual("Mine", spot.ContextVerb);
            var target = spot.Target;
            Assert.AreEqual(view.TopLevel(target.x, target.y), target.z, "the top block first");
            Assert.IsTrue(view.HighlightShown, "the block about to be mined is highlighted");
            yield return Capture.Take("outcrop_highlight");
            var carry = S.Logistics.System.CarryCount;
            S.Sites.SpeedMultiplier = 20f;
            HoldDig(true);
            yield return Until(() => Mine.IsSurfaceBroken(target.x, target.y, target.z), 10f, "the block breaks");
            HoldDig(false);
            S.Sites.SpeedMultiplier = 1f;
            Assert.Greater(S.Logistics.System.CarryCount, carry, "its ore went into the carry");
            Assert.AreEqual(target.z - 1, view.TopLevel(target.x, target.y), "the column is a block lower");
            yield return new WaitForSeconds(0.5f);
            yield return Capture.Take("surface_outcrop");
            CollectionAssert.IsEmpty(S.Logistics.System.Audit());
        }

        [UnityTest]
        public IEnumerator TheForestIsAGridOfTreesAboveTheMine()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var forest = Object.FindAnyObjectByType<ForestGridView>();
            var slab = MineSlabView.Current;
            var sim = s.Sites.Get("site.forest");
            var ground = Map("Ground");
            Assert.NotNull(forest);
            Assert.IsTrue(sim.GroveMode, "any tree can be cut");
            Assert.IsTrue(forest.CellOf(1, 0, out var firstCell));
            var first = ground.GetCellCenterWorld(firstCell);
            Assert.Greater(firstCell.y, GameRoot.Instance.Config.SandboxSettings.townCells.yMax, "the Forest stands on the frontier, beyond the town");
            var under = slab.SlabCoords(first);
            Assert.IsFalse(s.Sites.Get("site.mine").CanDig(Mathf.RoundToInt(under.x), Mathf.RoundToInt(under.y), 0), "and the topsoil under trees cannot be dug");

            // Tightly grown: standing trees block the way; only cut tiles can be walked.
            Assert.IsTrue(forest.HasStandingTree(firstCell));
            Assert.IsFalse(slab.SurfaceWalkable(first), "a standing tree blocks the supplier");
            for (var i = 0; i < 4000 && !sim.IsFelled(1, 0); i++) s.Sites.PlayerChopCell("site.forest", 1, 0, 0.5f);
            yield return null;
            Assert.IsFalse(forest.HasStandingTree(firstCell), "a felled tree leaves open ground");
            Assert.IsTrue(slab.SurfaceWalkable(first));

            // Further back in an area costs more; further left is the next area.
            Assert.IsTrue(forest.CellOf(1, 9, out var back));
            Assert.IsTrue(forest.CellOf(2, 0, out var left));
            Assert.Greater(ground.GetCellCenterWorld(back).y, first.y, "slots count back from the cliff edge");
            Assert.Less(ground.GetCellCenterWorld(left).x, first.x - 0.9f, "the next area is further left along the cliff");
            Assert.Greater(sim.TreeDurabilityMicro(1, 9), sim.TreeDurabilityMicro(1, 0));
            Assert.Greater(sim.TreeDurabilityMicro(2, 0), sim.TreeDurabilityMicro(1, 0));

            // The woodcutter's way into the frontier is open ground until the tree.
            var route = new List<Vector3>();
            forest.RouteToCell(Phase1PlayModeTests.Spot("container.forest_staging"), firstCell, route);
            for (var i = 1; i < route.Count - 1; i++)
                Assert.IsFalse(forest.HasStandingTree(ground.WorldToCell(route[i])), $"trail point {i} is open ground");
            Motor.Teleport(first);   // on the stump, among the standing trees
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(1.5f);
            foreach (var tree in forest.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!tree.name.StartsWith("Tree") && tree.name != "ForestTree") continue;
                Assert.AreEqual("Ground", tree.sortingLayerName, "trees sort among the Forest's blocks, by cell and level");
                Assert.Greater(tree.sortingOrder, ForestGridView.TerrainBase - 2000);
            }
            yield return Capture.Take("forest_frontier");
        }
    }
}
