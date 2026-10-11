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
    /// The Mine and the frontier Forest on one block grid (GDD 6/7): the shaft sinks layer by layer, tunnels run left
    /// at every level recessed behind a sill, the Forest above grows left and up, and the supplier can walk it all.
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
            if (GameRoot.Instance != null && GameRoot.Instance.InputRouter != null) GameRoot.Instance.InputRouter.ScriptedMove = null;
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
        private static void Stick(Vector2? v) => GameRoot.Instance.InputRouter.ScriptedMove = v;

        private static IEnumerator Hold(Vector2 v, Func<bool> until, float seconds, string what)
        {
            Stick(v);
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!until() && Time.realtimeSinceStartup < deadline) yield return null;
            Stick(null);
            Assert.IsTrue(until(), what);
        }

        private static IEnumerator StandNearMine(MineDigView dig)
        {
            Motor.Teleport(dig.PitTopCentre + new Vector3(1.2f, 2.2f, 0f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(1.5f);   // camera smoothing is real-time
        }

        private static Tilemap Map(string name) => GameObject.Find(name)?.GetComponent<Tilemap>();
        private static Tilemap Level(int level) => Map($"MineLevel{level}");

        private static int PitTiles(MineDigView dig, int level) => dig.DigOrder.Count(c => Level(level).HasTile(c));

        /// <summary>Finishes the shaft's current layer the ordinary way (fixture: the staging pile is emptied as if hauled).</summary>
        private static void FinishShaft(Ravenholm.Services.GameServices s)
        {
            s.Logistics.System.State.Containers["container.mine_staging"].Materials.Clear();
            s.Sites.DebugCompleteSection("site.mine");
            s.Simulation.Advance(120_000, 120_000);
            Assert.IsTrue(s.Sites.Get("site.mine").ShaftDone);
        }

        [UnityTest]
        public IEnumerator MinersSinkTheShaftBlockByBlockInView()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var dig = Object.FindAnyObjectByType<MineDigView>();
            Assert.NotNull(dig, "the Mine is dug into the ground");
            yield return StandNearMine(dig);
            Assert.AreEqual(1, dig.WorkingLevel, "the shaft starts at the town's own ground");
            Assert.AreEqual(MineDigView.Blocks, PitTiles(dig, 1), "the pit starts as solid ground");

            // Solo play: the supplier works the shaft from its rim, holding the button.
            Motor.Teleport(dig.LadderPlane + new Vector2(0.25f, 0.125f));
            yield return null;
            yield return null;
            var spot = MineActionSpot.Find(MineActionSpot.Kind.Shaft);
            Assert.IsTrue(spot.Available && spot.Holdable, "the shaft can be mined from the rim");
            Assert.AreSame(spot, SandboxSceneController.Current.ContextTarget);
            s.Sites.SpeedMultiplier = 6f;
            GameRoot.Instance.Hud.ScriptedHold = true;
            var deadline = Time.realtimeSinceStartup + 25f;
            while (dig.BlocksRemoved < 6 && Time.realtimeSinceStartup < deadline) yield return null;
            GameRoot.Instance.Hud.ScriptedHold = false;
            s.Sites.SpeedMultiplier = 1f;
            yield return null;
            Assert.IsTrue(Motor.Swinging || dig.BlocksRemoved >= 6, "the supplier swings");
            Assert.Greater(s.Logistics.System.CarryCount, 0, "ore goes straight into the carry");
            var level = dig.WorkingLevel;
            Assert.AreEqual(MineDigView.Blocks - dig.BlocksRemoved, PitTiles(dig, level), "each block of work removes one ground block");
            Assert.IsFalse(Level(level).HasTile(dig.DigOrder[0]), "the front block goes first, so the cut faces the camera");
            yield return Capture.Take("mine_dig_working");
        }

        [UnityTest]
        public IEnumerator EachLayerIsThreeLevelsWithItsOwnColour()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var dig = Object.FindAnyObjectByType<MineDigView>();
            var mine = s.Sites.Get("site.mine");
            yield return StandNearMine(dig);
            var cameraAtTop = Camera.main.transform.position.y;

            FinishShaft(s);
            Assert.IsTrue(s.Sites.ChooseDeeper("site.mine"), "Dig Deeper: the shaft sinks through the next layer");
            FinishShaft(s);
            yield return new WaitForSeconds(1.5f);

            Assert.AreEqual(7, dig.DeepestFloor, "two layers of three levels below the town's ground");
            Assert.AreEqual(2, mine.Record.Depth);
            var cam = Camera.main;
            Assert.AreEqual(cameraAtTop, cam.transform.position.y, 0.05f, "digging alone never moves the view");
            dig.ViewingDig = true;   // "Look down the shaft"
            yield return new WaitForSeconds(2f);
            Assert.Less(Mathf.Abs(cam.transform.position.y - dig.DigFocus.y), cam.orthographicSize * 0.5f, "looking down frames the working level");
            Assert.AreEqual(0, PitTiles(dig, 7), "dug levels are open");
            Assert.AreEqual(Level(5).color, Level(7).color, "a layer's three levels share its colour");
            Assert.AreNotEqual(Level(4).color, Level(5).color, "the next layer has its own");
            Assert.IsNotNull(Level(9), "the ground below shows");
            Assert.AreEqual(MineDigView.Blocks, PitTiles(dig, 8), "the next layer is solid until you dig deeper");
            Assert.Less(Level(9).color.grayscale, Level(7).color.grayscale, "undug ground is darker");
            yield return Capture.Take("mine_dig_levels");
        }

        [UnityTest]
        public IEnumerator TheTownEdgeIsTheSameBlockGridAsTheMine()
        {
            yield return Boot();
            var dig = Object.FindAnyObjectByType<MineDigView>();
            var ground = Map("Ground");
            yield return StandNearMine(dig);
            var edge = dig.TunnelPath[6] + Vector3Int.left;   // the cliff face beside the tunnel line
            Assert.IsTrue(ground.HasTile(edge) && !ground.HasTile(edge + Vector3Int.left), "a cell on the frontier's straight cliff face");
            Assert.IsTrue(Level(1).HasTile(edge), "the edge shows as a full ground block");
            Assert.IsTrue(Level(2).HasTile(edge), "and the cliff of layers continues below it");
            Assert.IsFalse(Level(2).HasTile(edge + new Vector3Int(3, 3, 0)), "hidden blocks inside the ground are never drawn");
            Assert.AreEqual(ground.GetCellCenterWorld(edge).y - 1f, Level(2).GetCellCenterWorld(edge).y, 0.001f, "one level = one block down");
            Assert.Greater(dig.TunnelCapacityCells, 80, "tunnels can run far left under the frontier");
        }

        [UnityTest]
        public IEnumerator TheLadderTakesThePlayerDownTheShaftAndBack()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var dig = Object.FindAnyObjectByType<MineDigView>();
            FinishShaft(s);
            yield return null;
            Assert.AreEqual(4, dig.DeepestFloor);

            // Walk up to the ladder from the town side: the camera follows smoothly, without jumps.
            var top = dig.LadderPlane + new Vector2(0.25f, 0.125f);
            Motor.Teleport(top + new Vector2(1.5f, 2.2f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(1.5f);
            var cam = Camera.main.transform;
            var maxStep = 0f;
            var last = cam.position;
            var prevDt = Time.unscaledDeltaTime;
            Stick((top - (Vector2)Motor.transform.position).normalized);
            var deadline = Time.realtimeSinceStartup + 6f;
            while (Vector2.Distance(Motor.transform.position, top) > 0.15f && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                // Coroutines resume before LateUpdate: what moved since the last check moved in the previous frame.
                maxStep = Mathf.Max(maxStep, Vector3.Distance(cam.position, last) / Mathf.Max(prevDt, 0.001f));
                prevDt = Time.unscaledDeltaTime;
                last = cam.position;
                Stick((top - (Vector2)Motor.transform.position).normalized);
            }
            Stick(null);
            Assert.Less(maxStep, 6f, "the camera moves no faster than a calm follow near the Mine (no snapping)");

            yield return Hold(Vector2.down, () => Motor.OnLadder, 2f, "pushing down at the ladder climbs onto it");
            yield return Hold(Vector2.down, () => Motor.ClimbHeight >= 1.99f, 4f, "down the shaft like a corridor");
            yield return Hold(Vector2.left, () => !Motor.OnLadder, 2f, "sideways steps off at a level");
            Assert.AreEqual(2, Motor.MineLevel);
            yield return Hold(Vector2.left, () => Motor.transform.position.x < dig.LadderPlane.x - 0.9f, 3f, "walks along the ledge");
            yield return new WaitForSeconds(1.5f);
            Assert.Less(Mathf.Abs(cam.position.y - Motor.transform.position.y), Camera.main.orthographicSize * 0.6f, "the camera followed down");
            yield return Capture.Take("mine_player_ledge");

            yield return Hold(Vector2.right, () => Vector2.Distance(Motor.transform.position, dig.LadderWorld(2)) < 0.6f, 3f, "back to the ladder");
            yield return Hold(Vector2.up, () => !Motor.OnLadder && Motor.MineLevel == 0, 5f, "up the ladder and off at the top");
            Assert.IsTrue(dig.SurfaceWalkable(Motor.transform.position), "back on the town's ground");
        }

        [UnityTest]
        public IEnumerator EachLevelDigsItsOwnRecessedTunnelLeft()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var dig = Object.FindAnyObjectByType<MineDigView>();
            var mine = s.Sites.Get("site.mine");
            FinishShaft(s);
            yield return null;

            // Climb down to level 2 (the layer's next level without a tunnel) and walk to the left end of its ledge: the context button offers its tunnel there.
            Motor.Teleport(dig.LadderPlane + new Vector2(0.25f, 0.125f));
            Motor.ClimbTo(2);
            var deadline = Time.realtimeSinceStartup + 5f;
            while ((Motor.OnLadder || Motor.MineLevel != 2) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(2, Motor.MineLevel, "the ladder's context action climbs to a level");
            yield return Hold(Vector2.left, () => SandboxSceneController.Current.ContextTarget is MineActionSpot sp && sp.SpotKind == MineActionSpot.Kind.Tunnel,
                4f, "walks the ledge to the tunnel end");
            var spot = MineActionSpot.Find(MineActionSpot.Kind.Tunnel);
            Assert.AreEqual("Dig", spot.ContextVerb);
            SandboxSceneController.Current.Inspect(spot);
            Assert.AreEqual(1, mine.Record.Tunnels.Count, "a tunnel is started at level 2");
            var t = mine.Record.Tunnels[0];
            Assert.AreEqual(2, t.Level);
            Assert.AreEqual(1, t.Depth, "in the level's own layer");

            // Hold Mine at its face: the supplier digs the tunnel themself.
            yield return Hold(Vector2.left, () => SandboxSceneController.Current.ContextTarget is MineActionSpot sp3 && sp3.ContextVerb == "Mine", 3f,
                "the tunnel face is in reach");
            GameRoot.Instance.Hud.ScriptedHold = true;
            yield return new WaitForSeconds(1.5f);
            GameRoot.Instance.Hud.ScriptedHold = false;
            Assert.Greater(t.DoneMicro, 0, "the supplier mined the tunnel face");

            // The level below digs its own tunnel at the same time, separated by a slab of dirt.
            Assert.AreEqual(2, s.Sites.StartTunnel("site.mine", 3));
            s.Logistics.System.State.Containers["container.mine_staging"].Materials.Clear();
            s.Logistics.System.DebugUnloadCarry();
            mine.DebugCompleteTunnel(1, toStaging: true);
            mine.DebugCompleteTunnel(2, toStaging: true);
            s.Simulation.Advance(120_000, 120_000);
            yield return new WaitForSeconds(0.5f);
            var path = dig.TunnelPath;
            Assert.AreEqual(4, dig.TunnelDug(2));
            for (var i = 0; i < 4; i++)
            {
                Assert.IsFalse(Level(2).HasTile(path[i]), "tunnel blocks are dug out");
                Assert.IsTrue(Map("MineSlabs2").HasTile(path[i]), "and stand on a slab of dirt");
            }
            Assert.Less(dig.FloorPoint(path[3], 2).x, dig.PitTopCentre.x - 2f, "the tunnel runs left");
            Assert.IsTrue(Level(2).HasTile(path[4]), "the next stretch is still ground");
            Assert.Greater(Map("MineSills2").GetUsedTilesCount(), 0, "the blocks in front are cut down to a sill: the tunnel sits one row in");
            Assert.IsFalse(Level(2).HasTile(path[1] + Vector3Int.left), "nothing full-height stands in front of the tunnel");
            Assert.IsTrue(Map("MineSills2").HasTile(path[1] + Vector3Int.left), "the cliff-face block in front is a sill");
            Assert.IsTrue(dig.Walkable(2, Level(1).GetCellCenterWorld(path[2])), "the player can walk into the tunnel");
            Assert.IsTrue(Map("MineSlabs3").HasTile(path[0]), "the level below has its own tunnel");

            yield return Hold(Vector2.left, () => SandboxSceneController.Current.ContextTarget is MineActionSpot sp2 && sp2.ContextVerb == "Extend", 5f,
                "at the tunnel face, the cleared stretch can be continued");
            SandboxSceneController.Current.Inspect(SandboxSceneController.Current.ContextTarget);
            Assert.AreEqual(1, t.Stretches);
            Assert.AreEqual(SiteStatus.Working, t.Status);
            yield return Capture.Take("mine_tunnels");
        }

        [UnityTest]
        public IEnumerator EveryChartedLayerAndOnlyNearbyLevelsAreBuilt()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var dig = Object.FindAnyObjectByType<MineDigView>();
            var mine = s.Sites.Get("site.mine");
            FinishShaft(s);
            while (mine.CanGoDeeper)
            {
                Assert.IsTrue(s.Sites.ChooseDeeper("site.mine"));
                FinishShaft(s);
            }
            dig.ViewingDig = true;   // "Look down the shaft"
            yield return new WaitForSeconds(2.5f);
            var deepest = mine.OpenLevels;
            Assert.AreEqual(1 + mine.LevelsPerBand * mine.Config.Bands.Count, deepest, "three levels per charted layer");
            Assert.IsNotNull(Level(deepest), "the bottom is built where the camera looks");
            Assert.IsNull(Level(3), "levels far from the camera are not kept");
            Assert.Less(dig.LevelsBuilt, deepest, "bounded work at any depth");
            var view = Camera.main;
            Assert.Less(Mathf.Abs(view.transform.position.y - dig.DigFocus.y), view.orthographicSize, "the bottom is on screen");
            yield return Capture.Take("mine_dig_deep");
        }

        // ------------------------------------------------------------------ the frontier Forest

        [UnityTest]
        public IEnumerator TheForestGrowsLeftAndUpAboveTheTunnels()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var forest = Object.FindAnyObjectByType<ForestGridView>();
            var dig = Object.FindAnyObjectByType<MineDigView>();
            var sim = s.Sites.Get("site.forest");
            var ground = Map("Ground");
            Assert.NotNull(forest);
            var first = ground.GetCellCenterWorld(forest.TreeCell(0));
            Assert.Greater(forest.TreeCell(0).y, GameRoot.Instance.Config.SandboxSettings.townCells.yMax, "the Forest stands on the frontier, beyond the town");
            Assert.AreEqual(dig.TunnelPath[0].x - 1, forest.TreeCell(0).x, "its first trees stand on the cliff edge, right above the tunnels");
            Assert.Less(first.y, dig.PitTopCentre.y + 1.5f, "starting at the bottom edge, right above the tunnels");

            // Tightly grown: standing trees block the way; only cut tiles can be walked.
            Assert.IsTrue(forest.HasStandingTree(forest.TreeCell(0)));
            Assert.IsFalse(dig.SurfaceWalkable(first), "a standing tree blocks the supplier");

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
            Motor.Teleport(new Vector2(-12.8f, -1.5f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(1.5f);
            yield return Capture.Take("forest_frontier");
        }
    }
}
