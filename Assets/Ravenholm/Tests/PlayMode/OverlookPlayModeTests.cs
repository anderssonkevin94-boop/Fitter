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
    /// The overlook (Astra's valley view, 2026-10-15): the view behind the north cliff, the castle on the far ridge, layers
    /// moving at their own rates as the camera moves (far slower than near), the lip stopping the walk, and the frames
    /// for Kevin: standing at the tip and walking along the cliff (Captures/overlook_*.png).
    /// </summary>
    public sealed class OverlookPlayModeTests
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
        public IEnumerator TheValleyViewMovesInLayersBehindTheCliff()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.NotNull(SandboxSceneController.Current);
            GameRoot.Instance.Hud.Tips.HideAll();   // a clear view for the pictures
            var view = ParallaxBackdrop.Current;
            Assert.NotNull(view, "the valley view is in the scene");
            Assert.NotNull(view.Castle, "with the castle on the far ridge");
            var tip = GameObject.Find("CliffCorner").transform.position;
            var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();

            // Standing at the tip, looking out.
            motor.Teleport((Vector2)tip + new Vector2(0f, -0.9f));
            yield return new WaitForSeconds(1.5f);
            yield return Capture.Take("overlook_standing");
            var castleA = view.Castle.position;
            var farA = view.LayerOrigin(1);
            var nearA = view.LayerOrigin(3);
            var camA = SandboxSceneController.Current.WorldCamera.transform.position;

            // Walking along the cliff (down-right along the right arm): frames for a little film.
            var router = GameRoot.Instance.InputRouter;
            router.ScriptedMove = new Vector2(1f, -0.5f).normalized;
            for (var i = 0; i < 8; i++)
            {
                yield return new WaitForSeconds(0.35f);
                yield return Capture.Take($"overlook_walk_{i}");
            }
            router.ScriptedMove = null;
            var camB = SandboxSceneController.Current.WorldCamera.transform.position;
            var moved = (Vector2)(camB - camA);
            Assert.Greater(moved.magnitude, 1f, "the camera followed the walk");
            var farMoved = view.LayerOrigin(1) - farA;
            var nearMoved = view.LayerOrigin(3) - nearA;
            // Relative to the camera, far layers drift less than near ones (screen offset = -displacement × factor).
            var farScreen = (farMoved - moved).magnitude;
            var nearScreen = (nearMoved - moved).magnitude;
            Assert.Less(farScreen, nearScreen, "far moves slower on screen than near");
            Assert.AreEqual(((Vector2)(view.Castle.position - castleA)).x, farMoved.x, 0.01f, "the castle rides the far ridge");

            // The lip stops the walk: pushing north from the tip never crosses it.
            motor.Teleport((Vector2)tip + new Vector2(0f, -0.6f));
            router.ScriptedMove = Vector2.up;
            yield return new WaitForSeconds(1.2f);
            router.ScriptedMove = null;
            Assert.Less(motor.transform.position.y, tip.y, "nobody walks off the cliff");
        }
    }
}
