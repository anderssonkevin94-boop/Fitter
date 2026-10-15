using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Ravenholm.Domain.Camp;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The journey, step 2 (2026-10-15): in build mode a station is picked and set up elsewhere on cleared ground (a short
    /// build, its delivery marker moving with it), packed into its kit and set up again.
    /// </summary>
    public sealed class StationPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-stations", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameRoot.Instance != null && GameRoot.Instance.Hud != null) GameRoot.Instance.Hud.CloseShopBuild();
            if (GameRoot.Instance != null) Object.Destroy(GameRoot.Instance.gameObject);
            yield return null;
            yield return null;
            GameRoot.SaveDirectoryOverride = null;
            try { Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        [UnityTest]
        public IEnumerator AStationIsMovedInBuildModeAndItsMarkerGoesWithIt()
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
            var smelter = StationSiteView.All["station.smelter"];
            var receiver = s.Logistics.System.State.Receivers["receiver.smelter"];
            var receiverFrom = new Vector2(receiver.X, receiver.Y);
            var from = (Vector2)smelter.transform.position;

            // Build mode: tap the Smelter to pick it.
            root.Hud.ToggleShopBuild();
            yield return null;
            Assert.IsTrue(root.Hud.ShopBuild.IsOpen);
            Assert.IsTrue(ShopView.Current.HandleTap(smelter.TapCollider.bounds.center));
            Assert.AreEqual("station.smelter", StationBuild.Selected, "tapping a station picks it");

            // Somewhere free in the clearing, a few steps from where it stood.
            (int x, int y) spot = default;
            var found = false;
            var site = s.Stations.Site("station.smelter");
            for (var r = 3; r < 14 && !found; r++)
            for (var dx = -r; dx <= r && !found; dx++)
            {
                var (x, y) = (site.X + dx, site.Y - r);
                if (s.Stations.CanPlace("station.smelter", x, y) == null) { spot = (x, y); found = true; }
            }
            Assert.IsTrue(found, "free ground in the clearing");
            var centre = StationSiteView.CentreOf(spot.x, spot.y, smelter.Footprint);
            Assert.IsTrue(ShopView.Current.HandleTap(centre));
            Assert.AreEqual(StationSiteMode.Placed, site.Mode, StationBuild.LastMessage);
            Assert.AreEqual(spot, (site.X, site.Y));
            yield return null;
            Assert.AreEqual(centre.x, smelter.transform.position.x, 0.01f, "the building moved");
            Assert.AreEqual(centre.y, smelter.transform.position.y, 0.01f);
            var moved = centre - from;
            receiver = s.Logistics.System.State.Receivers["receiver.smelter"];
            Assert.AreEqual(receiverFrom.x + moved.x, receiver.X, 0.02f, "its delivery marker moved with it");
            Assert.AreEqual(receiverFrom.y + moved.y, receiver.Y, 0.02f);
            Assert.AreEqual(0f, receiver.Radius, "it takes nothing while it is being built");
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(centre + new Vector2(0f, -2.2f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(1f);
            yield return Capture.Take("station_building");

            // Built: deliveries arrive at its new marker.
            yield return new WaitForSeconds(root.Services.Balance.stationBuildSeconds);
            Assert.IsFalse(s.Stations.Building("station.smelter"));
            receiver = s.Logistics.System.State.Receivers["receiver.smelter"];
            Assert.Greater(receiver.Radius, 0f, "built: it takes deliveries again");
            root.Hud.CloseShopBuild();
            var sys = s.Logistics.System;
            sys.RecordCreated("resource.iron_ore");
            Assert.IsTrue(sys.TryCarry("resource.iron_ore"));
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(new Vector2(receiver.X, receiver.Y));
            var t0 = Time.realtimeSinceStartup;
            while (sys.CarryCount > 0 && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            Assert.AreEqual(0, sys.CarryCount, "the ore went in at the new spot");
            yield return Capture.Take("station_moved");

            // Packed into its kit: gone from the camp, taking nothing; then set up again from the kit list.
            Assert.IsNull(s.Stations.Pack("station.smelter"));
            yield return null;
            Assert.IsFalse(smelter.gameObject.activeSelf, "packed away");
            Assert.AreEqual(0f, s.Logistics.System.State.Receivers["receiver.smelter"].Radius);
            root.Hud.ToggleShopBuild();
            yield return null;
            StationBuild.Select("station.smelter");
            Assert.IsTrue(ShopView.Current.HandleTap(StationSiteView.CentreOf(site.X, site.Y, smelter.Footprint)));
            yield return null;
            Assert.IsTrue(smelter.gameObject.activeSelf, "set up again");
            CollectionAssert.IsEmpty(sys.Audit());
        }
    }
}
