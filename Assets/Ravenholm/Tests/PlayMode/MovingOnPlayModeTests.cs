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
        public IEnumerator TheCompanyGoesThroughTheGateToTheNextLevel()
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
            Assert.IsTrue(s.Camp.Levels, "gated levels (2026-10-16)");
            var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            var trader = GameObject.Find("Trader Pad");
            var traderFrom = trader.transform.position;
            var ground = GameObject.Find("Grid").transform.Find("Ground").GetComponent<UnityEngine.Tilemaps.Tilemap>();
            Assert.NotNull(LevelView.Current, "the level painter is in the scene");

            // The castle's summons answered (the journey, step 5).
            s.Jobs.Board.DebugRecordSuccess("quest.roadside_patrol");
            s.Jobs.Board.DebugRecordSuccess("quest.old_shrine");
            // At the gate (the way there is through the old trees; here the supplier is simply put there).
            var gate = s.Camp.ExitWorld;
            motor.Teleport(gate);
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.6f);
            Assert.IsInstanceOf<MakeCampSpot>(SandboxSceneController.Current.ContextTarget, "the sign offers the way on");
            yield return Capture.Take("gate_sign");
            root.Hud.ShowMoveOn();
            yield return null;
            Assert.IsTrue(root.Hud.MoveOn.IsOpen);
            yield return Capture.Take("gate_panel");
            Assert.IsTrue(root.Hud.MoveOn.Press("Go through the gate"), root.Hud.MoveOn.Message);
            Assert.AreEqual(1, s.Camp.Index);

            // Level 2's ground is laid; the drive comes up from its front edge.
            yield return null;
            Assert.AreEqual(1, LevelView.Current.Painted, "level 2's ground");
            var r2 = s.Camp.Ridge;
            var e = r2.Extent;
            var only2 = 0;
            var wrong = 0;
            for (var x = e.minX; x <= e.maxX; x++)
            for (var y = e.minY; y <= e.maxY; y++)
            {
                var land = r2.InLand(x, y) && !r2.InProjectGap(x, y);
                if (land != ground.HasTile(new Vector3Int(x, y, 0))) wrong++;
                if (land && !s.Catalogue.CurrentCamp.RidgeFor(0).InLand(x, y)) only2++;
            }
            Assert.AreEqual(0, wrong, "the ground is level 2's land, cell for cell");
            Assert.Greater(only2, 20, "land that level 1 didn't have");
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(DriveView.Current.Driving, "the ox pulls the wagon in");
            yield return Capture.Take("gate_drive");
            deadline = Time.realtimeSinceStartup + 40f;
            while (DriveView.Current.Driving && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(DriveView.Current.Driving, "arrived");
            yield return new WaitForSeconds(1f);

            // The new level: the camp in the same place, the stations in kits, the Forest level 2's.
            Assert.AreEqual((Vector2)traderFrom, (Vector2)trader.transform.position, "the Trader came along, to the same place");
            foreach (var v in StationSiteView.All.Values) Assert.IsFalse(v.gameObject.activeSelf, $"{v.StationId} packed");
            var forest = Object.FindAnyObjectByType<ForestGridView>();
            var sim = s.Sites.Get("site.forest");
            Assert.AreSame(r2, sim.Config.Grove.Ridge);
            var tree = CampPlayModeTests.TreeAhead(sim, 10);
            Assert.AreEqual(2, sim.BandOf(tree.x, tree.y), "level 2's wood");
            Assert.IsTrue(forest.CellOf(tree.x, tree.y, out _), "its trees drawn");
            yield return Capture.Take("gate_arrived");
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }
    }
}
