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
    /// Moving on in the real scene (the journey, step 4): the sign in River Bend's clearing offers "Make camp", the
    /// drive up the road, and the wagon shop, the Trader and the Store's marker set down at the new camp; a station is set
    /// up there from its kit.
    /// </summary>
    public sealed class MovingOnPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-moveon", Guid.NewGuid().ToString("N"));
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
        [Ignore("Gated levels, step 2 (2026-10-16): moving on goes through the level's gate; rewritten then")]
        public IEnumerator TheCompanyMakesCampUpTheRoad()
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
            var trader = GameObject.Find("Trader Pad");
            var traderFrom = trader.transform.position;

            // The castle's summons answered (the journey, step 5).
            s.Jobs.Board.DebugRecordSuccess("quest.roadside_patrol");
            s.Jobs.Board.DebugRecordSuccess("quest.old_shrine");
            // In River Bend's clearing (the way there leads through the old trees; here the supplier is simply put there).
            var there = s.Camp.CentreOf(1);
            motor.Teleport(there + new Vector2(0.8f, -0.4f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.6f);
            Assert.IsInstanceOf<MakeCampSpot>(SandboxSceneController.Current.ContextTarget, "the sign offers Make camp");
            yield return Capture.Take("moveon_sign");
            root.Hud.ShowMoveOn();
            yield return null;
            Assert.IsTrue(root.Hud.MoveOn.IsOpen);
            StringAssert.Contains("River Bend", root.Hud.MoveOn.InfoText);
            yield return Capture.Take("moveon_panel");
            Assert.IsTrue(root.Hud.MoveOn.Press("Make camp here"), root.Hud.MoveOn.Message);
            Assert.AreEqual(1, s.Camp.Index);

            // The drive.
            yield return new WaitForSeconds(1.5f);
            Assert.IsTrue(DriveView.Current.Driving, "the ox pulls the wagon up the road");
            yield return Capture.Take("moveon_drive");
            deadline = Time.realtimeSinceStartup + 40f;
            while (DriveView.Current.Driving && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(DriveView.Current.Driving, "arrived");
            yield return new WaitForSeconds(1f);

            // The new camp: the shop, its counter's marker and the Trader moved with the camp; the stations are in kits.
            var offset = s.Camp.Offset;
            Assert.Less(Vector2.Distance(ShopView.Current.Wagon.position, there), 9f, "the wagon shop stands at River Bend");
            Assert.AreEqual((Vector2)traderFrom + offset, (Vector2)trader.transform.position, "the Trader came along");
            var store = s.Logistics.System.State.Receivers["receiver.store"];
            Assert.Less(Vector2.Distance(new Vector2(store.X, store.Y), there), 9f, "the Store's marker is at the new counter");
            foreach (var v in StationSiteView.All.Values) Assert.IsFalse(v.gameObject.activeSelf, $"{v.StationId} packed");
            yield return Capture.Take("moveon_arrived");

            // Set the Forge up from its kit in the new clearing.
            root.Hud.ToggleShopBuild();
            yield return null;
            StationBuild.Select("station.forge");
            var site = s.Stations.Site("station.forge");
            var placed = false;
            for (var r = 3; r < 12 && !placed; r++)
            for (var k = -r; k <= r && !placed; k++)
            {
                var c = s.Camp.CentreOf(1);
                var cell = StationBuild.CellAt(c) + new Vector2Int(k, -r);
                if (s.Stations.CanPlace("station.forge", cell.x, cell.y) != null) continue;
                Assert.IsTrue(ShopView.Current.HandleTap(StationSiteView.CentreOf(cell.x, cell.y, StationSiteView.All["station.forge"].Footprint)));
                placed = site.Mode == StationSiteMode.Placed;
            }
            Assert.IsTrue(placed, "a spot for the Forge in River Bend's clearing");
            yield return null;
            Assert.IsTrue(StationSiteView.All["station.forge"].gameObject.activeSelf);
            root.Hud.CloseShopBuild();
            yield return new WaitForSeconds(0.5f);
            yield return Capture.Take("moveon_forge");
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }
    }
}
