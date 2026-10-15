using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Presentation;
using Ravenholm.Services;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The road to the castle in the real scene (the journey, step 7): the ravine before Witchlight Fen is a gap in the land
    /// until the bridge is built from stock at its sign; built, its planks fill the gap and the supplier can walk across.
    /// Captures each camp up the road with its own land.
    /// </summary>
    public sealed class RoadPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-road", Guid.NewGuid().ToString("N"));
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

        [UnityTest]
        public IEnumerator TheBridgeIsBuiltAndWalkedAcross()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.NotNull(SandboxSceneController.Current);
            for (var i = 0; i < 3; i++) yield return null;
            var root = GameRoot.Instance;
            root.Hud.Tips.HideAll();
            var s = root.Services;
            var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            var mine = MineSlabView.Current;

            // Each camp up the road, with its own land.
            for (var c = 1; c < 6; c++)
            {
                motor.Teleport(s.Camp.CentreOf(c));
                SandboxSceneController.Current.SnapPlayerToSavedPosition();
                yield return new WaitForSeconds(0.6f);
                Assert.IsTrue(mine.SurfaceWalkable(s.Camp.CentreOf(c)), $"camp {c + 1}'s clearing is land");
                yield return Capture.Take($"road_camp{c + 1}");
            }

            // The ravine: a gap until built.
            var spot = Object.FindObjectsByType<GateProjectSpot>(FindObjectsSortMode.None).Single(p => p.Gate == 2);
            Assert.Greater(spot.GapCells.Count, 10, "a ravine across the land");
            var mid = spot.GapCells.OrderBy(c => (c - spot.GapCells[spot.GapCells.Count / 2]).sqrMagnitude).First();
            var midWorld = CampService.CellCentre(new Vector2(mid.x, mid.y));
            Assert.IsFalse(mine.SurfaceWalkable(midWorld), "nobody crosses the ravine yet");
            motor.Teleport(spot.transform.position + new Vector3(0.6f, -0.3f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.6f);
            Assert.AreSame(spot, SandboxSceneController.Current.ContextTarget, "the sign offers the bridge");
            yield return Capture.Take("road_ravine");
            spot.OnInspected();
            Assert.IsFalse(s.Camp.IsBuilt(2), "not without the timber and rope");

            s.Logistics.System.ProduceShared("resource.ironwood_lumber", 12, "test");
            s.Logistics.System.ProduceShared("resource.rope", 8, "test");
            spot.OnInspected();
            Assert.IsTrue(s.Camp.IsBuilt(2));
            yield return null;
            foreach (var c in spot.GapCells) Assert.IsTrue(mine.SurfaceWalkable(CampService.CellCentre(new Vector2(c.x, c.y))), $"plank at {c}");
            var forest = ForestGridView.Current;
            if (forest != null) Assert.IsTrue(forest.CanEnter(midWorld + new Vector2(-0.25f, -0.12f), midWorld), "no wall onto the bridge");
            motor.Teleport(midWorld);
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.6f);
            Assert.Less(Vector2.Distance(motor.transform.position, midWorld), 0.5f, "standing on the bridge, not sent back");
            yield return Capture.Take("road_bridge");
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }
    }
}
