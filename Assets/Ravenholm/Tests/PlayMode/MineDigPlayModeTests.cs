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
        private static readonly Vector2 Along = new Vector2(-2f, 1f).normalized;

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
        public IEnumerator DigDownTunnelAlongTheCliffAndClimbOutOnTheLadder()
        {
            yield return Boot();
            var slab = MineSlabView.Current;
            Assert.NotNull(slab, "the Mine is a dig slab");
            Assert.IsTrue(Mine.DigMode);
            Assert.Greater(slab.DrawnBlocks, 0, "the town's cliff edges are drawn as blocks");
            S.Sites.SpeedMultiplier = 20f;

            // The topsoil outside the entry cannot be dug; the entry can.
            Assert.IsFalse(Mine.CanDig(6, 10, 0));
            Assert.IsTrue(Mine.CanDig(0, 1, 0));
            Assert.IsFalse(Mine.CanDig(1, 1, 1), "the Mine is one slice of the cliff face");
            yield return DigDown(slab, 3);
            for (var r = 0; r < 3; r++) Assert.IsTrue(Mine.Ladder(0, 1, r), $"dug straight down: a ladder at row {r}");
            yield return null;
            Assert.AreEqual(1, slab.LadderCount, "one ladder for the whole shaft");
            Assert.AreEqual(3, slab.Feet, "three blocks down: room to stand, two blocks tall");
            Assert.Greater(S.Logistics.System.CarryCount, 0, "dug ore goes straight into the carry");
            Assert.IsTrue(Mine.Record.Revealed.ContainsKey(1), "the first layer is revealed by digging into it");

            // Tunnel along the cliff: walking into the wall with Dig held digs head height, then legs, then steps in.
            var startU = slab.U;
            Stick(Along);
            HoldDig(true);
            yield return Until(() => slab.U > startU + 3.5f, 40f, "tunnels along the cliff and walks into it");
            HoldDig(false);
            Stick(null);
            Assert.AreEqual(3, slab.Feet, "a level tunnel: no falling, no climbing");
            var lane = Mathf.RoundToInt(slab.X);
            var col = Mathf.RoundToInt(slab.U);
            Assert.IsTrue(Mine.IsDug(lane, col, 2) && Mine.IsDug(lane, col, 1), "the supplier stands in a two-high tunnel");
            Assert.IsNotNull(Mine.Record.LastDig, "the tunnel is remembered for routines");

            // Auto-target: the wall walked into (head height first, then legs), else the block underfoot.
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

            // Depth: nothing is in front of the supplier; the floor and the wall ahead are drawn behind them.
            yield return null;
            Assert.AreEqual(0, lane, "the tunnel is in the cliff face itself");
            Assert.Less(slab.DrawnOrder(lane, col, slab.Feet), slab.PlayerOrder, "the floor block is behind the supplier");

            // What can be dug wears a faint outline; pressing and holding on one digs it without the button.
            Assert.Greater(slab.OutlinedBlocks, 0, "reachable blocks are outlined");
            var inward = new DigCell(0, col + 1, slab.Feet);   // the floor one ahead
            Assert.IsTrue(Mine.CanDig(inward.X, inward.U, inward.Row) && slab.Reachable().Contains(inward), "the floor ahead is reachable");
            GameRoot.Instance.InputRouter.ScriptedPress = Camera.main.WorldToScreenPoint(slab.Top(inward.X, inward.U, inward.Row) + new Vector3(-0.15f, -0.35f, 0f));
            yield return Until(() => Mine.IsDug(inward.X, inward.U, inward.Row), 10f, "press and hold digs the block under the finger");
            GameRoot.Instance.InputRouter.ScriptedPress = null;
            Assert.IsFalse(slab.InReach(new DigCell(lane, col + 6, 2)), "blocks far away are out of reach");

            // Reading the rock: the Tap button rings for ore nearby; cracks show while a block is being dug.
            var rings = slab.Tap();
            Assert.Greater(slab.TapCooldownLeft, 0f, "the tap has a cooldown");
            Assert.AreEqual(rings, slab.LastTapRings);
            StringAssert.StartsWith("Tap…", slab.LastTapNote);
            var wall = Mine.CanDig(0, col + 2, slab.Feet - 1) ? new DigCell(0, col + 2, slab.Feet - 1) : new DigCell(0, col + 2, slab.Feet - 2);
            TapUntilSelected(slab, wall, new Vector3(-0.15f, -0.35f, 0f));
            yield return null;
            Assert.AreEqual(wall, slab.Target, "a tapped block becomes the block to dig");
            S.Sites.SpeedMultiplier = 1f;
            HoldDig(true);
            yield return Until(() => slab.CrackShowing, 5f, "the block cracks as it is dug");
            HoldDig(false);
            S.Sites.SpeedMultiplier = 20f;

            yield return new WaitForSeconds(0.6f);
            yield return Capture.Take("mine_dig_slab");

            // Back to the shaft, up its ladder, and off it onto the ground beside it.
            Stick(-Along);
            yield return Until(() => Mathf.RoundToInt(slab.U) == 1, 10f, "walks back to the shaft");
            Stick(Vector2.up);
            yield return Until(() => !slab.PlayerInside, 10f, () => $"climbs the ladder and out onto the ground (feet {slab.Feet}, on ladder {slab.OnLadder})");
            Stick(null);
            var at = (Vector2)SandboxSceneController.Current.Player.position;
            Assert.IsTrue(slab.SurfaceWalkable(at), $"back on the town's ground (at {at}, slab {slab.SlabCoords(at)}, ground {slab.OnGround(at)})");
            S.Sites.SpeedMultiplier = 1f;
        }

        [UnityTest]
        public IEnumerator ARoutineCarriesOnTheLastTunnel()
        {
            yield return Boot();
            var slab = MineSlabView.Current;
            Assert.IsFalse(S.Sites.StartRoutine("site.mine"), "nothing dug yet: no tunnel to carry on");
            S.Sites.SpeedMultiplier = 20f;
            yield return DigDown(slab, 3);
            Stick(Along);
            HoldDig(true);
            var startU = slab.U;
            yield return Until(() => slab.U > startU + 1.5f, 30f, "starts a tunnel along the cliff");
            HoldDig(false);
            Stick(null);
            S.Sites.SpeedMultiplier = 1f;

            Assert.IsTrue(S.Sites.StartRoutine("site.mine"));
            yield return null;
            yield return null;
            Assert.IsTrue(Motor.HiddenForRoutine, "the supplier is out on the routine");
            Assert.NotNull(WorkerView.PlayerAvatar, "drawn by the routine's figure");
            var dug = Mine.Record.Dug.Count;
            S.Simulation.Advance(600_000, 600_000);
            Assert.Greater(Mine.Record.Dug.Count, dug, "the routine digs on along the tunnel");
            var f = Mine.Record.RoutineFace;
            Assert.AreEqual(3, f.Feet, "on the same level");
            Assert.Greater(f.U, Mathf.RoundToInt(startU) + 1, "further along the cliff");

            // Moving takes over: the routine ends and the supplier is back beside the Mine's entry.
            Stick(Vector2.right);
            yield return Until(() => S.Sites.RoutineSiteId == null, 5f, "moving ends the routine");
            Stick(null);
            yield return null;
            Assert.IsFalse(Motor.HiddenForRoutine);
            Assert.IsFalse(slab.PlayerInside);
        }

        // ------------------------------------------------------------------ the frontier Forest

        [UnityTest]
        public IEnumerator TheForestGrowsLeftAndUpAboveTheTunnels()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var forest = Object.FindAnyObjectByType<ForestGridView>();
            var slab = MineSlabView.Current;
            var sim = s.Sites.Get("site.forest");
            var ground = Map("Ground");
            Assert.NotNull(forest);
            var first = ground.GetCellCenterWorld(forest.TreeCell(0));
            Assert.Greater(forest.TreeCell(0).y, GameRoot.Instance.Config.SandboxSettings.townCells.yMax, "the Forest stands on the frontier, beyond the town");
            var under = slab.SlabCoords(first);
            Assert.IsFalse(s.Sites.Get("site.mine").CanDig(Mathf.RoundToInt(under.x), Mathf.RoundToInt(under.y), 0), "and the topsoil under trees cannot be dug");

            // Tightly grown: standing trees block the way; only cut tiles can be walked.
            Assert.IsTrue(forest.HasStandingTree(forest.TreeCell(0)));

            Assert.IsFalse(slab.SurfaceWalkable(first), "a standing tree blocks the supplier");

            void Clear()
            {
                s.Logistics.System.State.Containers["container.forest_staging"].Materials.Clear();
                s.Sites.DebugCompleteSection("site.forest");
                s.Simulation.Advance(120_000, 120_000);
                Assert.AreEqual(SiteStatus.AwaitDecision, sim.Record.Status);
            }
            var firstCell = forest.TreeCell(0);
            Clear();
            Assert.IsTrue(s.Sites.ChooseContinue("site.forest"));
            yield return null;
            Assert.IsFalse(forest.HasStandingTree(firstCell), "felled trees leave open ground");
            var up = ground.GetCellCenterWorld(forest.TreeCell(0));
            Assert.Greater(up.y, first.y, "Continue Here cuts the next trees up the same area");
            Assert.AreEqual(first.x, up.x, 1.5f);
            Clear();
            Assert.IsTrue(s.Sites.ChooseDeeper("site.forest"));
            yield return null;
            var left = ground.GetCellCenterWorld(forest.TreeCell(0));
            Assert.Less(left.x, first.x - 0.9f, "Push Deeper moves one area further left");

            // The woodcutter's trail runs through cut ground only.
            var route = new List<Vector3>();
            forest.RouteTo(Phase1PlayModeTests.Spot("container.forest_staging"), 0, route);
            for (var i = 1; i < route.Count - 1; i++)
                Assert.IsFalse(forest.HasStandingTree(ground.WorldToCell(route[i])), $"trail point {i} is open ground");
            Motor.Teleport(ground.GetCellCenterWorld(firstCell));   // on a stump among the standing trees
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(1.5f);
            foreach (var tree in forest.GetComponentsInChildren<SpriteRenderer>())
                Assert.AreEqual(SpriteSortPoint.Pivot, tree.spriteSortPoint, "trees sort by their foot, so whoever stands behind is hidden");
            yield return Capture.Take("forest_frontier");
        }
    }
}
