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
    /// <summary>Phase 2 acceptance in the real scene: the supplier gathers by hand; nothing gathers while away.</summary>
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
        public IEnumerator NothingIsGatheredWhileYouAreAway()
        {
            yield return Boot();
            var services = GameRoot.Instance.Services;
            // Solo play, no AFK gathering (2026-10-15): no routine figures, no site signposts, no empty staging piles.
            Assert.IsNull(GameObject.Find("SiteMarker"), "no site markers");
            var mine = GameObject.Find("Mine");
            Assert.IsTrue(mine == null || mine.GetComponentsInChildren<Renderer>().Length == 0, "no Mine signpost");
            Assert.IsNull(GameObject.Find("Forest"), "no Forest signpost");
            foreach (var id in new[] { "container.mine_staging", "container.forest_staging" })
            {
                Assert.IsTrue(WorldContainerView.TryGet(id, out var pile));
                Assert.AreEqual(0, Staging(id));
                foreach (var r in pile.GetComponentsInChildren<Renderer>())
                    Assert.IsFalse(r.enabled && r.gameObject.activeInHierarchy && !(r.GetComponent<TMPro.TextMeshPro>() is TMPro.TextMeshPro t && string.IsNullOrEmpty(t.text)),
                        $"{id}: nothing drawn while empty ({r.name})");
                Assert.IsNull(pile.GetComponent<Inspectable>(), $"{id}: nothing to tap");
            }
            foreach (var s in services.Sites.Sites) Assert.AreEqual(0, s.Record.Workers.Count, $"{s.Config.SiteId}: nobody works it");

            var carry = services.Logistics.System.CarryCount;
            services.Simulation.Advance(600_000, 600_000);   // ten minutes away
            yield return null;
            Assert.AreEqual(0, Staging("container.mine_staging") + Staging("container.forest_staging"), "nothing arrived while away");
            Assert.AreEqual(carry, services.Logistics.System.CarryCount);
            CollectionAssert.IsEmpty(services.Logistics.System.Audit());
        }

        [UnityTest]
        public IEnumerator TheSupplierCutsAnyTreeInReach()
        {
            yield return Boot();
            var services = GameRoot.Instance.Services;
            var forest = services.Sites.Get("site.forest");
            var view = Object.FindAnyObjectByType<ForestGridView>();
            // Beside a tree ahead of the camp: any tree can be cut.
            var tree = CampPlayModeTests.TreeAhead(forest, 20, withNeighbour: true);
            Assert.IsTrue(view.CellOf(tree.x, tree.y, out var edge));
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(view.WorkSpotOf(edge));
            // The tree may lie past the first hairpin: the pass shows it once the supplier is near, then the forest redraws.
            yield return new WaitForSeconds(0.3f);
            var target = SandboxSceneController.Current.ContextTarget as ForestChopSpot;
            Assert.NotNull(target, "the context button targets the nearest tree");
            Assert.AreEqual("Chop", target.ContextVerb);
            Assert.IsTrue(forest.Config.Grove.InGrove(target.Area, target.Slot), "a tree of the forest ahead of the camp");
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
