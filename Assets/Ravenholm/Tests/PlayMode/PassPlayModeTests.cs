using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The switchback pass seen from the road (Kevin, 2026-10-16: "a nice view"): only the road the supplier is on is real
    /// ground; the stretch past the next hairpin is hidden (the layered valley shows there) until the supplier nears the
    /// hairpin, and that moves on round the turns. The valley view fills the screen behind the land, the castle on top.
    /// </summary>
    public sealed class PassPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-pass", Guid.NewGuid().ToString("N"));
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

        private static Vector3Int CellAt(Ravenholm.Domain.Sites.RidgeLayout ridge, double s)
        {
            var (x, y) = ridge.PointAt(s);
            return new Vector3Int(Mathf.RoundToInt((float)x), Mathf.RoundToInt((float)y), 0);
        }

        [UnityTest]
        [Ignore("Gated levels (2026-10-16): the switchback pass is retired; removed in step 3")]
        public IEnumerator ThePassPastTheNextHairpinIsHiddenUntilYouNearIt()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            for (var i = 0; i < 3; i++) yield return null;
            GameRoot.Instance.Hud.Tips.HideAll();
            var pass = PassView.Current;
            Assert.NotNull(pass, "the pass is drawn");
            var view = ParallaxBackdrop.Current;
            Assert.NotNull(view, "the valley view is behind the land");
            var ridge = GameRoot.Instance.Services.Catalogue.CurrentCamp.Ridge;
            var ground = GameObject.Find("Grid").transform.Find("Ground").GetComponent<UnityEngine.Tilemaps.Tilemap>();
            var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            yield return null;

            // At camp 1: the next leg (past the first hairpin) hidden, this one shown.
            Assert.AreEqual(0, pass.CurrentLeg, "at camp 1, on the first leg");
            Assert.AreEqual(ridge.TurnSpans[0].S0, pass.HiddenFrom, 1e-6, "hidden from the first hairpin");
            var next = CellAt(ridge, ridge.LegMid(1));
            Assert.IsTrue(ground.HasTile(next));
            Assert.Less(ground.GetColor(next).a, 0.5f, "leg 2's ground hidden");
            Assert.IsTrue(PassView.Painted(next));
            var here = CellAt(ridge, ridge.LegMid(0));
            Assert.Greater(ground.GetColor(here).a, 0.5f, "this leg is real ground");
            Assert.IsFalse(PassView.Painted(here));
            yield return Capture.Take("pass_view_camp1");

            // Near the hairpin: the next leg appears to walk onto; the one after stays hidden.
            var t0 = ridge.TurnSpans[0];
            motor.Teleport(IsoMap.World(CellAt(ridge, t0.S0 - 4)));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(ridge.TurnSpans[1].S0, pass.HiddenFrom, 1e-6, "near the hairpin: hidden from the next one");
            Assert.Greater(ground.GetColor(next).a, 0.5f, "leg 2 shown, ready to walk onto");
            Assert.IsTrue(PassView.Painted(CellAt(ridge, ridge.LegMid(2))), "leg 3 still hidden");
            yield return Capture.Take("pass_view_hairpin");

            // Along leg 2: it moves on a leg.
            motor.Teleport(IsoMap.World(CellAt(ridge, ridge.LegMid(1))));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(1, pass.CurrentLeg);
            Assert.AreEqual(ridge.TurnSpans[1].S0, pass.HiddenFrom, 1e-6);
            Assert.IsFalse(PassView.Painted(next));

            // Camp 3 (Kevin's view): the valley behind the land, nothing floating in front of it.
            var s = GameRoot.Instance.Services;
            s.Camp.DebugJumpTo(2);
            yield return null;
            deadline = Time.realtimeSinceStartup + 60f;
            while (DriveView.Current.Driving && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSeconds(1f);
            var c3 = IsoMap.Cell(motor.transform.position);
            var leg3 = ridge.LegIndexAt(ridge.Locate(c3.x, c3.y).s);
            Assert.AreEqual(2, leg3, "camp 3 sits on leg 3");
            Assert.AreEqual(2, pass.CurrentLeg);
            Assert.IsTrue(PassView.Painted(CellAt(ridge, ridge.LegMid(3))), "from camp 3, leg 4 hidden");
            yield return Capture.Take("pass_view_camp3");
            s.Camp.DebugJumpTo(0);
            yield return null;
        }
    }
}
