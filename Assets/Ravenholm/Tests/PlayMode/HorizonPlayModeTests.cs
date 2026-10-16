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
    /// The horizon ahead (the journey, 2026-10-15): Astra's valley view as a band at the top of the screen behind the land,
    /// the castle on its far ridge, showing where the ridge ends, layers sliding sideways at their own rates (far slower than
    /// near), and the frames for Kevin: standing in camp and walking forward (Captures/horizon_*.png).
    /// </summary>
    public sealed class HorizonPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-overlook", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameRoot.Instance != null) Object.Destroy(GameRoot.Instance.gameObject);
            yield return null;
            GameRoot.SaveDirectoryOverride = null;
            try { Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        [UnityTest]
        public IEnumerator TheCastleStandsOnTheHorizonBehindTheLand()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.NotNull(SandboxSceneController.Current);
            GameRoot.Instance.Hud.Tips.HideAll();   // a clear view for the pictures
            var view = ParallaxBackdrop.Current;
            Assert.NotNull(view, "the horizon is in the scene");
            Assert.NotNull(view.Castle, "with the castle on the far ridge");
            var cam = SandboxSceneController.Current.WorldCamera;
            var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            yield return new WaitForSeconds(1.5f);
            // (Batch mode's own screen has another shape than the phone's, so positions are checked in the world, not the viewport.)
            Assert.Greater(view.Castle.position.y, cam.transform.position.y, "the castle stands on the far ridge, up the screen");
            Assert.AreEqual(cam.transform.position.x, view.Castle.position.x, 1.5f, "straight ahead");
            Assert.Less(view.Castle.GetComponent<SpriteRenderer>().sortingOrder, ForestGridView.TerrainBase - 10000, "behind the land: it shows only where the ridge ends");
            Assert.Greater(cam.transform.position.y, motor.transform.position.y + 1.5f, "the supplier sits low, with the road ahead on screen");
            yield return Capture.Take("horizon_standing");
            var castleA = view.Castle.position;
            var farA = view.LayerOrigin(1);
            var nearA = view.LayerOrigin(3);
            var camA = cam.transform.position;
            var topA = view.TopY - camA.y;

            // Walking forward (up-right): frames for a little film.
            var router = GameRoot.Instance.InputRouter;
            router.ScriptedMove = new Vector2(1f, 0.5f).normalized;
            for (var i = 0; i < 8; i++)
            {
                yield return new WaitForSeconds(0.35f);
                yield return Capture.Take($"horizon_walk_{i}");
            }
            router.ScriptedMove = null;
            var camB = cam.transform.position;
            var moved = (Vector2)(camB - camA);
            Assert.Greater(moved.magnitude, 1f, "the camera followed the walk");
            var farScreen = Mathf.Abs((view.LayerOrigin(1) - farA).x - moved.x);
            var nearScreen = Mathf.Abs((view.LayerOrigin(3) - nearA).x - moved.x);
            Assert.Less(farScreen, nearScreen, "sideways, far moves slower on screen than near");
            Assert.AreEqual(((Vector2)(view.Castle.position - castleA)).x, (view.LayerOrigin(1) - farA).x, 0.01f, "the castle rides the far ridge");
            Assert.AreEqual(topA, view.TopY - camB.y, 0.01f, "the horizon stays at the top of the screen");
        }
    }
}
