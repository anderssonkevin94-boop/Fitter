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
    /// The switchback pass seen from the road (Astra's pass pack, 2026-10-16): the leg the supplier is on and the next are
    /// real ground; from the turn after the next leg the pass is painted (the real ground there hidden), and that moves on
    /// as the supplier goes round the turns. The valley floor fills what lies beyond the edges; the castle ends the road.
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

        private static IEnumerator Wide(string name, float ortho)
        {
            var cam = SandboxSceneController.Current.WorldCamera;
            var follow = cam.GetComponent<FollowCamera>();
            if (follow != null) follow.enabled = false;
            var size = cam.orthographicSize;
            cam.orthographicSize = ortho;
            yield return Capture.Take(name);
            cam.orthographicSize = size;
            if (follow != null) follow.enabled = true;
        }

        [UnityTest]
        public IEnumerator TheFarLegsArePaintedAndMoveOnRoundTheTurns()
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
            var ridge = GameRoot.Instance.Services.Catalogue.CurrentCamp.Ridge;
            var ground = GameObject.Find("Grid").transform.Find("Ground").GetComponent<UnityEngine.Tilemaps.Tilemap>();
            var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            yield return null;
            Assert.AreEqual(0, pass.CurrentLeg, "at camp 1, on the first leg");
            Assert.AreEqual(ridge.TurnSpans[1].S0, pass.PaintedFrom, 1e-6, "painted from the turn after the next leg");
            Assert.Greater(pass.ModulesShown, 10, "the legs further on as paintings");

            // The real ground there is hidden (the painting shows); before it, shown.
            var (fx, fy) = ridge.PointAt(ridge.LegMid(3));
            var far = new Vector3Int(Mathf.RoundToInt((float)fx), Mathf.RoundToInt((float)fy), 0);
            Assert.IsTrue(ground.HasTile(far));
            Assert.Less(ground.GetColor(far).a, 0.5f, "leg 4's real ground hidden behind its painting");
            Assert.IsTrue(PassView.Painted(far));
            var (nx, ny) = ridge.PointAt(ridge.LegMid(1));
            var near = new Vector3Int(Mathf.RoundToInt((float)nx), Mathf.RoundToInt((float)ny), 0);
            Assert.Greater(ground.GetColor(near).a, 0.5f, "the next leg is real ground");
            Assert.IsFalse(PassView.Painted(near));
            yield return Wide("pass_from_camp1_wide", 16f);

            // On round the first turn and along leg 2: the paintings move on a leg.
            var (mx, my) = ridge.PointAt(ridge.LegMid(1));
            motor.Teleport(IsoMap.World((float)mx, (float)my));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(1, pass.CurrentLeg);
            Assert.AreEqual(ridge.TurnSpans[2].S0, pass.PaintedFrom, 1e-6);
            Assert.IsTrue(PassView.Painted(far), "from leg 2, leg 4 is still two on: painted");
            var (tx, ty) = ridge.PointAt(ridge.LegMid(2));
            var next = new Vector3Int(Mathf.RoundToInt((float)tx), Mathf.RoundToInt((float)ty), 0);
            Assert.Greater(ground.GetColor(next).a, 0.5f, "leg 3, now the next leg, is real ground again");
            yield return Capture.Take("pass_leg2");
            yield return Wide("pass_leg2_wide", 16f);
        }
    }
}
