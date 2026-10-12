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
        public IEnumerator ARoutineCutsNearTheLastTreeAndHaulsToStaging()
        {
            yield return Boot();
            var services = GameRoot.Instance.Services;
            var forest = services.Sites.Get("site.forest");
            var player = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            player.Teleport(new Vector2(1.5f, -5.5f));   // stand away from the piles so we do not pick them up
            services.Sites.SpeedMultiplier = 20f;

            // Solo play: the supplier leaves themself on a woodcutting routine; it carries on beside the last tree cut.
            Assert.IsFalse(services.Sites.StartRoutine("site.forest"), "nothing cut yet");
            for (var i = 0; i < 4000 && !forest.IsFelled(1, 0); i++) services.Sites.PlayerChopCell("site.forest", 1, 0, 0.1f);
            services.Logistics.System.DebugUnloadCarry();
            Assert.IsTrue(services.Sites.StartRoutine("site.forest"));
            yield return null;
            var cutter = WorkerView.PlayerAvatar;
            Assert.NotNull(cutter, "the supplier's routine figure appears");
            Assert.IsTrue(player.HiddenForRoutine, "and the supplier is out working it");
            var start = cutter.transform.position;
            var furthest = 0f;
            yield return WaitUntil(() =>
            {
                furthest = Mathf.Max(furthest, Vector3.Distance(start, cutter.transform.position));
                return Staging("container.forest_staging") > 0;
            }, 10f, "first deposits");
            Assert.Greater(furthest, 0.5f, "the routine visibly walks to the trees and back");
            Assert.AreEqual(SiteStatus.Working, forest.Record.Status, "nothing waits for a choice: cutting is the choice");
            CollectionAssert.IsEmpty(services.Logistics.System.Audit());
            yield return Capture.Take("phase2_forest_working");

            GameRoot.Instance.Hud.ShowSite("site.forest");
            yield return null;
            Assert.IsTrue(GameRoot.Instance.Hud.SitePanel.IsOpen);
            Assert.IsTrue(TouchInputRouter.IsBlocked, "site panel is modal for movement");
            StringAssert.Contains("Cut:", GameRoot.Instance.Hud.SitePanel.StatusText);
            yield return Capture.Take("phase2_forest_panel");
            GameRoot.Instance.Hud.SitePanel.Close();
            Assert.IsFalse(TouchInputRouter.IsBlocked);

            // Moving takes over again.
            GameRoot.Instance.InputRouter.ScriptedMove = Vector2.right;
            yield return null;
            yield return null;
            GameRoot.Instance.InputRouter.ScriptedMove = null;
            Assert.IsNull(services.Sites.RoutineSiteId, "moving ends the routine");
            Assert.IsFalse(player.HiddenForRoutine);
            CollectionAssert.IsEmpty(services.Logistics.System.Audit());
        }

        [UnityTest]
        public IEnumerator TheSupplierCutsAnyTreeInReach()
        {
            yield return Boot();
            var services = GameRoot.Instance.Services;
            var forest = services.Sites.Get("site.forest");
            var view = Object.FindAnyObjectByType<ForestGridView>();
            // Beside a tree a few back from the cliff edge, not the first one: any tree can be cut.
            Assert.IsTrue(view.CellOf(1, 1, out var edge));
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(view.WorkSpotOf(edge));
            yield return null;
            yield return null;
            var target = SandboxSceneController.Current.ContextTarget as ForestChopSpot;
            Assert.NotNull(target, "the context button targets the nearest tree");
            Assert.AreEqual("Chop", target.ContextVerb);
            Assert.AreEqual(1, target.Area);
            var (area, slot, cell) = (target.Area, target.Slot, target.Cell);
            services.Sites.SpeedMultiplier = 20f;
            GameRoot.Instance.Hud.ScriptedHold = true;   // hold the button
            yield return WaitUntil(() => forest.IsFelled(area, slot), 10f, "the tree is felled");
            GameRoot.Instance.Hud.ScriptedHold = false;
            Assert.GreaterOrEqual(services.Logistics.System.CarryCount, 4, "its logs went straight into the carry");
            Assert.IsFalse(view.HasStandingTree(cell), "a stump: the tile can be walked now");
            yield return null;
            yield return null;
            var next = SandboxSceneController.Current.ContextTarget as ForestChopSpot;
            Assert.NotNull(next, "the next tree in reach is offered straight away");
            Assert.IsFalse(next.Area == area && next.Slot == slot);
            yield return Capture.Take("phase2_forest_clearing");
            CollectionAssert.IsEmpty(services.Logistics.System.Audit());
        }
    }
}
