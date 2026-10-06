using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Ravenholm.Domain.Sites;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>Phase 2 acceptance in the real scene: workers, staging, markers, site panels and decisions.</summary>
    public sealed class Phase2PlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-phase2", Guid.NewGuid().ToString("N"));
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

        private static IEnumerator WaitUntil(Func<bool> condition, float realSeconds, string what)
        {
            var end = Time.realtimeSinceStartup + realSeconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(condition(), "timed out waiting for: " + what);
        }

        private static int Staging(string id) => GameRoot.Instance.Services.Logistics.System.State.Containers[id].UnitCount;

        [UnityTest]
        public IEnumerator WorkersHaulToStagingAndDecisionsAreThePlayers()
        {
            yield return Boot();
            var services = GameRoot.Instance.Services;
            var player = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            player.Teleport(new Vector2(-7.6f, 2.2f));   // stand away from the pile so we do not pick it up
            services.Sites.SpeedMultiplier = 20f;

            var miner = GameObject.Find("Dagna Holt").transform;
            var start = miner.position;
            yield return WaitUntil(() => Staging("container.mine_staging") > 0 && Staging("container.forest_staging") > 0, 6f, "first deposits");
            Assert.AreNotEqual(start, miner.position, "the miner visibly moved");
            CollectionAssert.IsEmpty(services.Logistics.System.Audit());
            yield return Capture.Take("phase2_mine_working");

            services.Sites.DebugCompleteSection("site.mine");
            var mine = services.Sites.Get("site.mine");
            yield return WaitUntil(() => mine.Record.Status == SiteStatus.AwaitDecision, 10f, "mine section exhausted");
            var marker = GameObject.Find("Mine").GetComponentInChildren<SiteMarkerView>(true);
            yield return null;
            Assert.IsTrue(marker.Showing);
            Assert.AreEqual("Choose next section", marker.Text);
            yield return Capture.Take("phase2_choose_marker");

            // Workers finish returning an already-earned pack (GDD 5), then wait.
            yield return WaitUntil(() => mine.Record.Workers.TrueForAll(w => w.State == WorkerState.AwaitDepthChoice), 10f, "miner waiting at staging");
            var depositedBefore = mine.Record.DepositedTotal;
            yield return new WaitForSeconds(1f);   // 20 s of sim time
            Assert.AreEqual(depositedBefore, mine.Record.DepositedTotal, "nothing happens until the player decides");
            Assert.AreEqual(1, mine.Record.Depth);

            GameRoot.Instance.Hud.ShowSite("site.mine");
            yield return null;
            Assert.IsTrue(GameRoot.Instance.Hud.SitePanel.IsOpen);
            Assert.IsTrue(TouchInputRouter.IsBlocked, "site panel is modal for movement");
            yield return Capture.Take("phase2_mine_panel");

            Assert.IsTrue(services.Sites.ChooseDeeper("site.mine"));
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(2, mine.Record.Depth);
            Assert.AreEqual(SiteStatus.Working, mine.Record.Status);
            yield return Capture.Take("phase2_mine_panel_depth2");
            GameRoot.Instance.Hud.SitePanel.Close();
            Assert.IsFalse(TouchInputRouter.IsBlocked);
            CollectionAssert.IsEmpty(services.Logistics.System.Audit());
        }

        [UnityTest]
        public IEnumerator ForestClearingAndPanel()
        {
            yield return Boot();
            var services = GameRoot.Instance.Services;
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(new Vector2(7.6f, 3.4f));
            services.Sites.SpeedMultiplier = 20f;
            var forest = services.Sites.Get("site.forest");
            yield return WaitUntil(() => forest.Record.Trees.Exists(t => t.Felled), 10f, "first tree felled");
            yield return Capture.Take("phase2_forest_clearing");

            GameRoot.Instance.Hud.ShowSite("site.forest");
            yield return new WaitForSeconds(0.3f);
            StringAssert.Contains("Clearing:", GameRoot.Instance.Hud.SitePanel.StatusText);
            yield return Capture.Take("phase2_forest_panel");
            GameRoot.Instance.Hud.SitePanel.Close();
            CollectionAssert.IsEmpty(services.Logistics.System.Audit());
        }
    }
}
