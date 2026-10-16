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
        public IEnumerator EachCampStandsByTheDropWithItsGroupOnLand()
        {
            // Kevin, 2026-10-16: each camp looks over the drop to the castle (the land's edge a little right of the group).
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            for (var i = 0; i < 3; i++) yield return null;
            var root = GameRoot.Instance;
            root.Hud.Tips.HideAll();
            var s = root.Services;
            var ground = GameObject.Find("Grid").transform.Find("Ground").GetComponent<UnityEngine.Tilemaps.Tilemap>();
            var road = s.Catalogue.CurrentCamp;
            for (var c = 0; c < road.CampCount; c++)
            {
                if (c > 0)
                {
                    s.Camp.DebugJumpTo(c);
                    yield return null;
                    deadline = Time.realtimeSinceStartup + 60f;
                    while (DriveView.Current.Driving && Time.realtimeSinceStartup < deadline) yield return null;
                    yield return new WaitForSeconds(0.5f);
                }
                // Everything the camp brings stands on land: the wagon shop, the Trader's pad, the staging piles.
                var group = Object.FindObjectsByType<CampAnchored>(FindObjectsSortMode.None).Select(a => a.transform).ToList();
                group.Add(ShopView.Current.transform);
                foreach (var t in group)
                foreach (var sr in t.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!sr.enabled || sr.sortingLayerName == "Overlay") continue;
                    var cell = ground.WorldToCell(sr.transform.position);
                    Assert.IsTrue(ground.HasTile(cell), $"camp {c + 1}: {sr.transform.parent?.name}/{sr.name} stands over the drop at {cell}");
                }
                // The drop is in view, a little right of the camp's middle.
                var mid = (Vector2)ground.GetCellCenterWorld(Vector3Int.RoundToInt((Vector3)road.CampCentre(c)));
                var dropAt = -1f;
                for (var k = 0.5f; k < 6f && dropAt < 0f; k += 0.25f)
                    if (!ground.HasTile(ground.WorldToCell(mid + new Vector2(k, 0f)))) dropAt = k;
                Assert.That(dropAt, Is.InRange(1.5f, 3.4f), $"camp {c + 1}: the drop is on screen right of the camp");
                var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
                motor.Teleport(mid);
                SandboxSceneController.Current.SnapPlayerToSavedPosition();
                yield return new WaitForSeconds(0.6f);
                yield return Capture.Take($"edge_camp{c + 1}");
            }
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

            // The developer tools' camp jump: up the road to Hollow Wood (the drive), then straight back to camp 1.
            s.Camp.DebugJumpTo(2);
            yield return null;
            deadline = Time.realtimeSinceStartup + 60f;
            while (DriveView.Current.Driving && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSeconds(0.5f);
            Assert.Less(Vector2.Distance(motor.transform.position, s.Camp.CentreOf(2)), 9f, "the supplier arrived at Hollow Wood");
            yield return Capture.Take("road_jump_hollow_wood");
            s.Camp.DebugJumpTo(0);
            yield return null;
            deadline = Time.realtimeSinceStartup + 5f;
            while (DriveView.Current.Driving && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(DriveView.Current.Driving, "no drive back down the road");
            yield return new WaitForSeconds(0.3f);
            Assert.Less(Vector2.Distance(motor.transform.position, s.Camp.CentreOf(0)), 9f, "back at Meadow's Edge");
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }
    }
}
