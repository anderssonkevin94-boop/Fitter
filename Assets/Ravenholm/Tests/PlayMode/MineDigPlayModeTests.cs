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
            Assert.AreEqual(Level(1).color, Level(2).color, "a continued layer keeps its colour");
            Assert.AreNotEqual(Level(2).color, Level(3).color, "a new layer has its own colour");
            Assert.AreEqual(0, PitTiles(dig, 3), "dug levels are open");
            Assert.AreEqual(MineDigView.Blocks, PitTiles(dig, 4), "the working level is solid until worked");
            Assert.IsNotNull(Level(5), "the next levels show below");
            Assert.Less(Level(5).color.grayscale, Level(4).color.grayscale, "previews are darker");
            var cam = Camera.main;
            var player = SandboxSceneController.Current.Player.position;
            Assert.Less(Mathf.Abs(cam.transform.position.y - player.y), cam.orthographicSize, "near the Mine the supplier stays in view");
            Assert.LessOrEqual(cam.transform.position.y, cameraAtTop + 0.01f, "and the view never rises away from the dig");
            dig.ViewingDig = true;   // "Look down the shaft"
            yield return new WaitForSeconds(2f);
            Assert.Less(Mathf.Abs(cam.transform.position.y - dig.DigFocus.y), cam.orthographicSize * 0.5f, "looking down frames the working level");
            yield return Capture.Take("mine_dig_levels");
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
