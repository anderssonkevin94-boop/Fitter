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
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>The Mine cutaway (GDD 6): fixed-size window, layers scroll, miners visible in the shaft.</summary>
    public sealed class MineCutawayPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-mine", Guid.NewGuid().ToString("N"));
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

        private static IEnumerator StandNearMine(MineCutawayView cut)
        {
            var p = cut.World(MineCutawayView.Footprint * 0.5f, MineCutawayView.Footprint + 0.9f, 0f);
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(p);
            for (var i = 0; i < 40; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator MinersWorkVisiblyInTheShaftAndTheFloorDrops()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var cut = Object.FindAnyObjectByType<MineCutawayView>();
            Assert.NotNull(cut, "the Mine is a cutaway");
            yield return StandNearMine(cut);
            s.Sites.SpeedMultiplier = 6f;
            var dagna = Object.FindObjectsByType<WorkerView>(FindObjectsInactive.Exclude).First(w => w.WorkerId == "worker.dagna");
            var deadline = Time.realtimeSinceStartup + 20f;
            while (!dagna.InShaft && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(dagna.InShaft, "the miner climbs down into the shaft and stays visible");
            Assert.IsTrue(dagna.transform.Find("Visual").gameObject.activeSelf);
            var floorBefore = cut.FloorDepth;
            deadline = Time.realtimeSinceStartup + 20f;
            while (cut.SectionProgress < 0.5f && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSeconds(0.5f);
            Assert.Greater(cut.FloorDepth, floorBefore + 0.05f, "the floor drops as the section is worked");
            s.Sites.SpeedMultiplier = 1f;
            yield return Capture.Take("mine_cutaway_working");
        }

        [UnityTest]
        public IEnumerator DiggingDeeperScrollsLayersInsideAFixedWindow()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var cut = Object.FindAnyObjectByType<MineCutawayView>();
            yield return StandNearMine(cut);
            var mesh = cut.transform.Find("Shell").GetComponent<MeshFilter>().sharedMesh;
            var sizeAtDepth1 = mesh.bounds.size;
            var mine = s.Sites.Get("site.mine");
            for (var d = 0; d < 4; d++)
            {
                s.Logistics.System.State.Containers["container.mine_staging"].Materials.Clear();   // fixture: hauled away
                s.Sites.DebugCompleteSection("site.mine");
                s.Simulation.Advance(120_000, 120_000);
                if (mine.Record.Status == SiteStatus.AwaitDecision && mine.CanGoDeeper) Assert.IsTrue(s.Sites.ChooseDeeper("site.mine"));
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);
            Assert.GreaterOrEqual(mine.Record.Depth, 4);
            Assert.AreEqual(mine.Record.Depth, cut.ShownDepth);
            Assert.AreEqual(4, cut.HistoryBandsShown, "three recent layers plus one merged band, however deep");
            Assert.AreEqual(sizeAtDepth1.x, mesh.bounds.size.x, 0.01f, "the block never grows: a fixed window");
            Assert.AreEqual(sizeAtDepth1.y, mesh.bounds.size.y, 0.01f);
            s.Logistics.System.State.Containers["container.mine_staging"].Materials.Clear();   // fixture: someone hauled it away
            s.Sites.DebugCompleteSection("site.mine");
            s.Simulation.Advance(120_000, 120_000);
            Assert.IsTrue(s.Sites.ChooseContinue("site.mine"), "a worked-out section offers the choice");
            yield return null;
            yield return null;
            Assert.AreEqual(1, cut.TunnelStubs, "continue here opens a side tunnel");
            yield return Capture.Take("mine_cutaway_depth");
        }
    }
}
