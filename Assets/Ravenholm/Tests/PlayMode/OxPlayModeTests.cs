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
    /// Astra's ox, eight-direction walk V12 (Kevin approved, 2026-10-16): every direction at one scale on its own ground
    /// anchor (turning never shifts him), the six-frame walk looping in 1.2 seconds, the standing pose when idle; hitched
    /// at the parked wagon (solid), and pulling it on the drive.
    /// </summary>
    public sealed class OxPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-ox", Guid.NewGuid().ToString("N"));
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
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            for (var i = 0; i < 3; i++) yield return null;
            GameRoot.Instance.Hud.Tips.HideAll();
        }

        [UnityTest]
        public IEnumerator EightDirectionsStandAndWalkOnTheirGroundAnchors()
        {
            yield return Boot();
            var parked = ShopView.Current.Ox;
            Assert.NotNull(parked, "the ox stands hitched at the parked wagon");
            Assert.IsFalse(parked.Walking, "standing while the wagon is parked");
            Assert.AreEqual("NE", parked.Dir, "facing up the road");
            Assert.NotNull(Physics2D.OverlapPoint((Vector2)parked.transform.position + new Vector2(0f, 0.12f)), "solid: no walking through him");

            // One ox in every direction, on one ground point near the supplier.
            var player = SandboxSceneController.Current.Player;
            var at = (Vector2)player.position + new Vector2(1.3f, 0.6f);
            var art = parked;   // the same art the game uses
            var ox = Object.Instantiate(parked.gameObject).GetComponent<OxFigure>();
            Object.Destroy(ox.transform.Find("OxBody")?.gameObject);
            Object.DestroyImmediate(ox.GetComponent<OxWander>());   // held in place for the pictures
            ox.transform.position = at;
            ox.transform.localScale = Vector3.one;
            Vector2? size = null;
            foreach (var d in new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" })
            {
                ox.FaceDir(d);
                ox.Walking = false;
                yield return null;
                var sr = ox.Renderer;
                Assert.AreEqual((Vector2)at, (Vector2)sr.transform.position, $"{d}: on the same ground point");
                // The sprite's pivot is its direction's ground anchor; every direction the same size (one pixels-per-unit).
                var b = sr.sprite.bounds.size;
                if (size == null) size = b;
                Assert.AreEqual(size.Value.x, b.x, 1e-3f, $"{d}: one scale for every direction");
                Assert.AreEqual(1280f / 720f, b.x, 1e-3f, "the full canvas at 720 px per unit");
                Assert.IsFalse(sr.flipX, $"{d}: the pack's own mirrors, never flipped again");
                yield return Capture.Take($"ox_{d}_stand");
                ox.Walking = true;
                var first = sr.sprite;
                yield return new WaitForSeconds(0.45f);
                Assert.AreNotSame(first, sr.sprite, $"{d}: walking");
                yield return Capture.Take($"ox_{d}_walk");
            }
            // The loop: six frames at 5 fps come round in 1.2 seconds.
            ox.FaceDir("SW");
            ox.Walking = false;
            yield return null;
            ox.Walking = true;
            yield return null;
            var start = ox.Renderer.sprite;
            yield return new WaitForSeconds(1.2f);
            Assert.AreSame(start, ox.Renderer.sprite, "back to the first frame after 1.2 seconds");
            Object.Destroy(ox.gameObject);

            // Pulling the wagon on the drive (gated levels: into the next level from its front edge).
            GameRoot.Instance.Services.Camp.DebugJumpTo(1);
            yield return new WaitForSeconds(1.2f);
            Assert.IsTrue(DriveView.Current.Driving);
            var walking = DriveView.Current.GetComponentInChildren<OxFigure>();
            Assert.NotNull(walking, "the ox pulls the wagon");
            Assert.IsTrue(walking.Walking);
            yield return Capture.Take("ox_drive");
        }

        [UnityTest]
        public IEnumerator TheParkedOxAmblesAboutNearTheWagon()
        {
            yield return Boot();
            var ox = ShopView.Current.Ox;
            var wander = ox.GetComponent<OxWander>();
            Assert.NotNull(wander, "the parked ox wanders");
            var home = wander.Home;
            var blockers = LayerMask.GetMask(PlayerMotor.BlockerLayerName);
            var body = ox.GetComponentInChildren<Collider2D>();
            var moved = 0f;
            var walked = false;
            for (var round = 0; round < 4; round++)
            {
                wander.WalkNow();
                var deadline = Time.time + 8f;
                yield return null;
                yield return null;
                while (wander.Walking && Time.time < deadline)
                {
                    walked |= ox.Walking;
                    yield return null;
                }
                var at = (Vector2)ox.transform.position;
                moved = Mathf.Max(moved, Vector2.Distance(at, home));
                var off = at - home;
                Assert.LessOrEqual(Mathf.Pow(off.x / (wander.RadiusX + 0.05f), 2) + Mathf.Pow(off.y / (wander.RadiusY + 0.05f), 2), 1f, "he stays near the wagon");
                Assert.IsFalse(ox.Walking, "standing between walks");
                Assert.AreEqual(body.transform.position, ox.transform.position, "his blocker goes with him");
                if (round == 1) yield return Capture.Take("ox_wander");
            }
            Assert.IsTrue(walked, "his walk plays while he moves");
            Assert.Greater(moved, 0.4f, "he moved about");
        }
    }
}
