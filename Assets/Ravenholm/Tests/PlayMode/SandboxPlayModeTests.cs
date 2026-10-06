using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Ravenholm.Infrastructure.Save;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Boots the real Bootstrap -> DebugSandbox flow against a temporary save folder.
    /// Captures land in &lt;project&gt;/Captures (git-ignored) for visual review.
    /// </summary>
    public sealed class SandboxPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-playmode", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return KillRoot();
            GameRoot.SaveDirectoryOverride = null;
            SafeAreaFitter.NormalizedOverride = null;
            WorldLabel.Visible = false;
            CarryStackFixture.Visible = true;
            FollowCamera.ViewWidthIndex = -1;
            try { Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        private static IEnumerator Boot()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox") && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.NotNull(SandboxSceneController.Current, "DebugSandbox did not finish loading");
            yield return null;
            yield return null;
        }

        /// <summary>Simulates an app restart: the composition root and all its services go away.</summary>
        private static IEnumerator KillRoot()
        {
            if (GameRoot.Instance != null)
            {
                GameRoot.Instance.Services?.Sandbox.Flush("test restart");
                Object.Destroy(GameRoot.Instance.gameObject);
            }
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator BootstrapComposesServicesAndLoadsSandbox()
        {
            yield return Boot();
            var root = GameRoot.Instance;
            Assert.NotNull(root);
            Assert.IsTrue(root.Services.ContentReport.IsValid, root.Services.ContentReport.ToString());
            Assert.NotNull(root.Hud);
            Assert.AreEqual(1, Object.FindObjectsByType<GameRoot>().Length, "exactly one composition root");
            Assert.AreEqual(SaveLoadStatus.NoSave, root.Services.Saves.Status.LoadStatus);
            Assert.AreEqual(1, root.Services.Saves.Status.Generation, "session start was saved");
        }

        [UnityTest]
        public IEnumerator PressingPlayInSandboxRoutesThroughBootstrap()
        {
            SceneManager.LoadScene("DebugSandbox");
            yield return null;
            var deadline = Time.realtimeSinceStartup + 10f;
            while (SandboxSceneController.Current == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.NotNull(GameRoot.Instance, "guard should have loaded Bootstrap");
            Assert.AreEqual("DebugSandbox", SceneManager.GetActiveScene().name);
        }

        [UnityTest]
        public IEnumerator TapOnBellRingsItAndValueSurvivesRestart()
        {
            yield return Boot();
            var controller = SandboxSceneController.Current;
            var bell = Object.FindAnyObjectByType<BellInspectable>();
            Assert.NotNull(bell);

            // Real tap path: screen point -> physics overlap -> frontmost inspectable.
            var cam = controller.WorldCamera;
            var col = bell.GetComponent<Collider2D>();
            var screen = cam.WorldToScreenPoint(col.bounds.center);
            var onTapped = typeof(SandboxSceneController).GetMethod("OnTapped", BindingFlags.NonPublic | BindingFlags.Instance);
            onTapped.Invoke(controller, new object[] { (Vector2)screen });
            Assert.AreEqual(1, GameRoot.Instance.Services.Sandbox.State.BellRingCount);
            Assert.IsTrue(GameRoot.Instance.Hud.IsInfoOpen, "inspect card opens");
            Assert.IsTrue(TouchInputRouter.IsBlocked, "movement is blocked under a modal");
            GameRoot.Instance.Hud.HideInfo();
            Assert.IsFalse(TouchInputRouter.IsBlocked);

            controller.Inspect(bell);
            controller.Inspect(bell);
            GameRoot.Instance.Hud.HideInfo();
            Assert.AreEqual(3, GameRoot.Instance.Services.Sandbox.State.BellRingCount);

            yield return KillRoot();
            yield return Boot();
            var state = GameRoot.Instance.Services.Sandbox.State;
            Assert.AreEqual(3, state.BellRingCount, "bell count survived restart");
            Assert.AreEqual(2, state.SessionCount);
            StringAssert.Contains("Bell rung <b>3</b>", GameRoot.Instance.Hud.PersistenceText);
        }

        [UnityTest]
        public IEnumerator CorruptSaveFallsBackSafelyOnBoot()
        {
            yield return Boot();
            var bell = Object.FindAnyObjectByType<BellInspectable>();
            SandboxSceneController.Current.Inspect(bell);   // gen 2
            SandboxSceneController.Current.Inspect(bell);   // gen 3, backup holds gen 2 (1 ring)
            GameRoot.Instance.Hud.HideInfo();
            GameRoot.Instance.Services.Saves.Store.DebugCorruptPrimary();
            Object.Destroy(GameRoot.Instance.gameObject);    // no flush: simulate a crash right after corruption
            yield return null;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("RecoveredFromBackup"));
            yield return Boot();
            var services = GameRoot.Instance.Services;
            Assert.AreEqual(SaveLoadStatus.RecoveredFromBackup, services.Saves.Status.LoadStatus);
            Assert.AreEqual(1, services.Sandbox.State.BellRingCount);
            Assert.IsNull(services.Saves.Status.LastError);
            Assert.IsNotEmpty(Directory.GetFiles(_dir, "profile.sav.corrupt-*"), "corrupt file kept for diagnosis");
        }

        [UnityTest]
        public IEnumerator PortraitCapturesInTwoAspectRatios()
        {
            yield return Boot();
            // iPhone 16 Pro (19.5:9, Dynamic Island) and iPhone SE (16:9, no notch).
            yield return CaptureAndCheck("iphone16pro", 1206, 2622, new Rect(0f, 102f / 2622f, 1f, 1f - (186f + 102f) / 2622f));
            yield return CaptureAndCheck("iphoneSE", 750, 1334, new Rect(0, 0, 1, 1));

            WorldLabel.Visible = true;
            yield return CaptureAndCheck("iphone16pro_labels", 1206, 2622, new Rect(0f, 102f / 2622f, 1f, 1f - (186f + 102f) / 2622f));
            WorldLabel.Visible = false;

            var bell = Object.FindAnyObjectByType<BellInspectable>();
            SandboxSceneController.Current.Inspect(bell);
            yield return CaptureAndCheck("iphone16pro_inspect", 1206, 2622, new Rect(0f, 102f / 2622f, 1f, 1f - (186f + 102f) / 2622f));
            GameRoot.Instance.Hud.HideInfo();

            GameRoot.Instance.Hud.OpenDebug();
            yield return CaptureAndCheck("iphoneSE_debug", 750, 1334, new Rect(0, 0, 1, 1));
            GameRoot.Instance.Hud.CloseDebug();

            // Overview of the whole diorama at the widest debug framing.
            FollowCamera.ViewWidthIndex = 2;
            var player = GameObject.Find("Player").transform;
            foreach (var spot in new[] { new Vector2(-7f, 2f), new Vector2(7f, 2f) })
            {
                player.position = spot;
                yield return CaptureAndCheck($"iphone16pro_view_{(spot.x < 0 ? "mine" : "forest")}", 1206, 2622,
                    new Rect(0f, 102f / 2622f, 1f, 1f - (186f + 102f) / 2622f));
            }
        }

        private static IEnumerator CaptureAndCheck(string name, int w, int h, Rect safeArea)
        {
            SafeAreaFitter.NormalizedOverride = safeArea;
            var cam = SandboxSceneController.Current.WorldCamera;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            for (var i = 0; i < 4; i++) yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;

            // HUD anchors must land inside the screen at this aspect ratio.
            foreach (var path in new[] { "TopBar", "PersistenceCard", "Context" })
            {
                var rect = FindRect(GameRoot.Instance.Hud.transform, path);
                Assert.NotNull(rect, path);
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                foreach (var c in corners)
                {
                    var sp = cam.WorldToScreenPoint(c);
                    Assert.That(sp.x, Is.InRange(-1f, w + 1f), $"{name}: {path} off-screen horizontally");
                    Assert.That(sp.y, Is.InRange(-1f, h + 1f), $"{name}: {path} off-screen vertically");
                }
            }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;

            var dir = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Captures"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
        }

        private static RectTransform FindRect(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<RectTransform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
