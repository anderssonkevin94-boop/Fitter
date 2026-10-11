using System;
using System.Collections;
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
    /// <summary>The Mine dug into the town's ground (GDD 6): level by level, block by block, camera following down.</summary>
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

        private static IEnumerator StandNearMine(MineDigView dig)
        {
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(dig.PitTopCentre + new Vector3(0f, 1.9f, 0f));
            yield return new WaitForSeconds(1.5f);   // camera smoothing is real-time
        }

        private static Tilemap Level(int level) => GameObject.Find($"MineLevel{level}")?.GetComponent<Tilemap>();

        private static int PitTiles(MineDigView dig, int level) => dig.DigOrder.Count(c => Level(level).HasTile(c));

        /// <summary>Finishes the current section the ordinary way (fixture: the staging pile is emptied as if hauled).</summary>
        private static void FinishSection(Ravenholm.Services.GameServices s)
        {
            s.Logistics.System.State.Containers["container.mine_staging"].Materials.Clear();
            s.Sites.DebugCompleteSection("site.mine");
            s.Simulation.Advance(120_000, 120_000);
        }

        [UnityTest]
        public IEnumerator MinersClearTheLevelBlockByBlockInView()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var dig = Object.FindAnyObjectByType<MineDigView>();
            Assert.NotNull(dig, "the Mine is dug into the ground");
            yield return StandNearMine(dig);
            Assert.AreEqual(1, dig.WorkingLevel);
            Assert.AreEqual(MineDigView.Blocks, PitTiles(dig, 1), "the pit starts as solid ground");

            s.Sites.SpeedMultiplier = 6f;
            var dagna = Object.FindObjectsByType<WorkerView>(FindObjectsInactive.Exclude).First(w => w.WorkerId == "worker.dagna");
            var deadline = Time.realtimeSinceStartup + 25f;
            while (!(dagna.InShaft && dig.BlocksRemoved >= 6) && Time.realtimeSinceStartup < deadline) yield return null;
            s.Sites.SpeedMultiplier = 1f;
            yield return null;
            Assert.IsTrue(dagna.InShaft, "the miner works down in the pit");
            Assert.IsTrue(dagna.transform.Find("Visual").gameObject.activeSelf, "and stays visible");
            Assert.GreaterOrEqual(dig.BlocksRemoved, 6);
            Assert.AreEqual(MineDigView.Blocks - dig.BlocksRemoved, PitTiles(dig, 1), "each block of work removes one ground block");
            Assert.IsFalse(Level(1).HasTile(dig.DigOrder[0]), "the front block goes first, so the cut faces the camera");
            Assert.AreEqual(dig.StandSpot(0).y, Level(1).GetCellCenterWorld(dig.DigOrder[dig.BlocksRemoved]).y, 0.01f, "standing on the next block to clear");
            yield return Capture.Take("mine_dig_working");
        }

        [UnityTest]
        public IEnumerator EachSectionDigsOneLevelDownWithItsLayerColour()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var dig = Object.FindAnyObjectByType<MineDigView>();
            var mine = s.Sites.Get("site.mine");
            yield return StandNearMine(dig);
            var cameraAtTop = Camera.main.transform.position.y;   // the test runner's view is small and landscape

            FinishSection(s);
            Assert.IsTrue(s.Sites.ChooseContinue("site.mine"), "continue: same layer, next level down");
            FinishSection(s);
            Assert.IsTrue(s.Sites.ChooseDeeper("site.mine"), "deeper: new layer, next level down");
            FinishSection(s);
            Assert.IsTrue(s.Sites.ChooseDeeper("site.mine"));
            yield return new WaitForSeconds(1.5f);

            Assert.AreEqual(4, dig.WorkingLevel, "three sections = three levels dug");
            CollectionAssert.AreEqual(new[] { 1, 1, 2 }, mine.Record.SectionDepths, "levels remember their layer");
            Assert.AreEqual(Color.white, Level(1).color, "level 1 is the town's own ground");
            Assert.AreEqual(dig.Tint(1, 0), Level(2).color, "a continued layer keeps its colour");
            Assert.AreNotEqual(Level(2).color, Level(3).color, "a new layer has its own colour");
            Assert.AreEqual(0, PitTiles(dig, 3), "dug levels are open");
            Assert.AreEqual(MineDigView.Blocks, PitTiles(dig, 4), "the working level is solid until worked");
            var cam = Camera.main;
            var player = SandboxSceneController.Current.Player.position;
            Assert.Less(Mathf.Abs(cam.transform.position.y - player.y), cam.orthographicSize, "the camera follows the supplier, not the dig");
            Assert.AreEqual(cameraAtTop, cam.transform.position.y, 0.05f, "digging alone never moves the view");
            dig.ViewingDig = true;   // "Look down the shaft"
            yield return new WaitForSeconds(2f);
            Assert.Less(Mathf.Abs(cam.transform.position.y - dig.DigFocus.y), cam.orthographicSize * 0.5f, "looking down frames the working level");
            Assert.IsNotNull(Level(5), "the next levels show below");
            Assert.Less(Level(5).color.grayscale, Level(4).color.grayscale, "previews are darker");
            yield return Capture.Take("mine_dig_levels");
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

        [UnityTest]
        public IEnumerator TheTownEdgeIsTheSameBlockGridAsTheMine()
        {
            yield return Boot();
            var dig = Object.FindAnyObjectByType<MineDigView>();
            var ground = GameObject.Find("Ground").GetComponent<Tilemap>();
            yield return StandNearMine(dig);
            // A front-edge town cell away from the Mine: its level-1 block (full face) sits under the town tile, and the
            // cliff continues below it, one block per level, on the same grid.
            var edge = dig.TunnelPath(Ravenholm.Domain.Sites.TunnelSide.Right).Skip(6).First(c => !ground.HasTile(c + Vector3Int.left) || !ground.HasTile(c + Vector3Int.down));
            Assert.IsTrue(ground.HasTile(edge), "the tunnel path runs along the town's own edge cells");
            var path = dig.TunnelPath(Ravenholm.Domain.Sites.TunnelSide.Right);
            Assert.IsTrue(Level(1).HasTile(edge), "the town's edge shows as a full ground block: " +
                string.Join(" ", path.Take(10).Select(c => $"{c}:{ground.HasTile(c)}/{ground.HasTile(c + Vector3Int.left)}/{ground.HasTile(c + Vector3Int.down)}/{Level(1).HasTile(c)}")) +
                $" levels {string.Join(",", Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None).Select(t => t.name + ":" + t.GetUsedTilesCount()))}");
            Assert.IsTrue(Level(2).HasTile(edge), "and the cliff of layers continues below it");
            var inner = edge + new Vector3Int(3, 3, 0);
            Assert.IsFalse(Level(2).HasTile(inner), "hidden blocks inside the ground are never drawn");
            Assert.AreEqual(ground.GetCellCenterWorld(edge).y - 1f, Level(2).GetCellCenterWorld(edge).y, 0.001f, "one level = one block down");
        }

        [UnityTest]
        public IEnumerator TheLadderTakesThePlayerDownTheShaftAndBack()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var dig = Object.FindAnyObjectByType<MineDigView>();
            FinishSection(s);
            Assert.IsTrue(s.Sites.ChooseContinue("site.mine"));
            FinishSection(s);
            Assert.IsTrue(s.Sites.ChooseContinue("site.mine"));
            yield return null;
            Assert.AreEqual(2, dig.DeepestFloor);

            // Walk up to the ladder from the town side: the camera follows smoothly, without jumps.
            var top = dig.LadderPlane + new Vector2(-0.25f, 0.125f);
            Motor.Teleport(top + new Vector2(-1.5f, 2.2f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(1.5f);
            var cam = Camera.main.transform;
            var maxStep = 0f;
            var last = cam.position;
            var trace = new System.Text.StringBuilder();
            var prevDt = Time.unscaledDeltaTime;
            Stick((top - (Vector2)Motor.transform.position).normalized);
            var deadline = Time.realtimeSinceStartup + 6f;
            while (Vector2.Distance(Motor.transform.position, top) > 0.15f && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                // Coroutines resume before LateUpdate: what moved since the last check moved in the previous frame.
                maxStep = Mathf.Max(maxStep, Vector3.Distance(cam.position, last) / Mathf.Max(prevDt, 0.001f));
                prevDt = Time.unscaledDeltaTime;
                trace.Append($"{Time.unscaledDeltaTime:0.000}:{cam.position.x:0.00},{cam.position.y:0.00}/{Motor.transform.position.x:0.00},{Motor.transform.position.y:0.00} ");
                last = cam.position;
                Stick((top - (Vector2)Motor.transform.position).normalized);
            }
            Stick(null);
            Assert.Less(maxStep, 6f, "the camera moves no faster than a calm follow near the Mine (no snapping): " + trace);

            yield return Hold(Vector2.down, () => Motor.OnLadder, 2f, "pushing down at the ladder climbs onto it");
            yield return Hold(Vector2.down, () => Motor.ClimbHeight >= 1.99f, 4f, "down the shaft like a corridor");
            yield return Hold(Vector2.right, () => !Motor.OnLadder, 2f, "sideways steps off at a level");
            Assert.AreEqual(2, Motor.MineLevel);
            yield return Hold(Vector2.right, () => Motor.transform.position.x > dig.LadderPlane.x + 0.9f, 3f, "walks along the ledge");
            yield return new WaitForSeconds(1.5f);
            Assert.Less(Mathf.Abs(cam.position.y - Motor.transform.position.y), Camera.main.orthographicSize * 0.6f, "the camera followed down");
            yield return Capture.Take("mine_player_ledge");

            yield return Hold(Vector2.left, () => Vector2.Distance(Motor.transform.position, dig.LadderWorld(2)) < 0.6f, 3f, "back to the ladder");
            yield return Hold(Vector2.up, () => !Motor.OnLadder && Motor.MineLevel == 0, 5f, "up the ladder and off at the top");
            Assert.IsTrue(dig.SurfaceWalkable(Motor.transform.position), "back on the town's ground");
        }

        [UnityTest]
        public IEnumerator EachLevelDigsItsOwnTunnelAlongTheFront()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var dig = Object.FindAnyObjectByType<MineDigView>();
            var mine = s.Sites.Get("site.mine");
            FinishSection(s);
            Assert.IsTrue(s.Sites.ChooseContinue("site.mine"));
            FinishSection(s);
            Assert.IsTrue(s.Sites.ChooseDeeper("site.mine"));
            FinishSection(s);
            Assert.IsTrue(s.Sites.ChooseContinue("site.mine"));
            yield return null;

            // Climb down to level 2 and walk to the right end of its ledge: the context button offers a tunnel there.
            Motor.Teleport(dig.LadderPlane + new Vector2(-0.25f, 0.125f));
            Motor.ClimbTo(2);
            var deadline = Time.realtimeSinceStartup + 5f;
            while ((Motor.OnLadder || Motor.MineLevel != 2) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(2, Motor.MineLevel, "the ladder's context action climbs to a level");
            var end = dig.FloorPoint(dig.LedgeCells[dig.LedgeCells.Count - 1], 2);
            yield return Hold(Vector2.right, () => Vector2.Distance(Motor.transform.position, end) < 1.2f ||
                                                   SandboxSceneController.Current.ContextTarget is MineActionSpot sp && sp.SpotKind == MineActionSpot.Kind.TunnelRight,
                3f, "walks the ledge toward the right end");
            yield return null;
            var spot = MineActionSpot.Find(MineActionSpot.Kind.TunnelRight);
            Assert.IsTrue(spot.Available);
            Assert.AreEqual("Dig", spot.ContextVerb);
            SandboxSceneController.Current.Inspect(spot);
            Assert.AreEqual(1, mine.Record.Tunnels.Count, "a tunnel is started at level 2, right");
            var t = mine.Record.Tunnels[0];
            Assert.AreEqual(2, t.Level);
            Assert.AreEqual(1, t.Depth, "dug in level 2's own layer");
            Assert.AreEqual(1, mine.Record.Workers[0].Face, "the miner was sent to it");

            // A second level's tunnel exists in parallel, separated from the first by a slab of dirt.
            Assert.AreEqual(2, s.Sites.StartTunnel("site.mine", 3, Ravenholm.Domain.Sites.TunnelSide.Right));

            s.Logistics.System.State.Containers["container.mine_staging"].Materials.Clear();
            mine.DebugCompleteTunnel(1);
            mine.DebugCompleteTunnel(2);
            s.Simulation.Advance(120_000, 120_000);
            yield return new WaitForSeconds(0.5f);
            var path = dig.TunnelPath(Ravenholm.Domain.Sites.TunnelSide.Right);
            Assert.AreEqual(4, dig.TunnelDug(2, Ravenholm.Domain.Sites.TunnelSide.Right));
            var slabs2 = GameObject.Find("MineSlabs2").GetComponent<Tilemap>();
            for (var i = 0; i < 4; i++)
            {
                Assert.IsFalse(Level(2).HasTile(path[i]), "tunnel blocks are dug out");
                Assert.IsTrue(slabs2.HasTile(path[i]), "and stand on a slab of dirt");
            }
            Assert.IsTrue(Level(2).HasTile(path[4]), "the next block of the tunnel is still ground");
            Assert.IsTrue(Level(2).HasTile(path[1] + Vector3Int.up) || Level(2).HasTile(path[1] + Vector3Int.right), "the tunnel's back wall shows");
            Assert.IsTrue(dig.Walkable(2, Level(1).GetCellCenterWorld(path[2])), "the player can walk into the tunnel");
            Assert.IsTrue(GameObject.Find("MineSlabs3").GetComponent<Tilemap>().HasTile(path[0]), "the level below has its own tunnel");

            yield return Hold(Vector2.right, () => SandboxSceneController.Current.ContextTarget is MineActionSpot sp2 && sp2.ContextVerb == "Extend", 4f,
                "at the tunnel face, the cleared stretch can be extended");
            SandboxSceneController.Current.Inspect(SandboxSceneController.Current.ContextTarget);
            Assert.AreEqual(1, t.Stretches);
            Assert.AreEqual(SiteStatus.Working, t.Status);
            yield return Capture.Take("mine_tunnels");
        }

        [UnityTest]
        public IEnumerator DepthIsUnlimitedAndOnlyNearbyLevelsAreBuilt()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var dig = Object.FindAnyObjectByType<MineDigView>();
            for (var i = 0; i < 30; i++)
            {
                FinishSection(s);
                Assert.IsTrue(s.Sites.ChooseContinue("site.mine"));
            }
            dig.ViewingDig = true;   // "Look down the shaft"
            yield return new WaitForSeconds(2.5f);
            Assert.AreEqual(31, dig.WorkingLevel);
            Assert.IsNotNull(Level(31), "the working level is built where the camera looks");
            Assert.IsNull(Level(5), "levels far from the camera are not kept");
            Assert.Less(dig.LevelsBuilt, 30, "bounded work at any depth");
            var view = Camera.main;
            Assert.Less(Mathf.Abs(view.transform.position.y - dig.DigFocus.y), view.orthographicSize, "the working level is on screen");
            yield return Capture.Take("mine_dig_deep");
        }
    }
}
