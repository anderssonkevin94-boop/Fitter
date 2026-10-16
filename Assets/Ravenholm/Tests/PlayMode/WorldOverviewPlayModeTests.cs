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
    /// <summary>A zoomed-out picture of the whole world (for layout work): Captures/world_overview.png.</summary>
    public sealed class WorldOverviewPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-overview", Guid.NewGuid().ToString("N"));
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
        public IEnumerator CaptureTheWholeWorld()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.NotNull(SandboxSceneController.Current);
            var cam = SandboxSceneController.Current.WorldCamera;
            var follow = cam.GetComponent<FollowCamera>();
            if (follow != null) follow.enabled = false;
            var b = GameRoot.Instance.Config.SandboxSettings.WorldBounds;
            cam.transform.position = new Vector3(b.center.x, b.center.y, cam.transform.position.z);
            const int w = 2400, h = 2400;
            cam.orthographicSize = Mathf.Max(b.height, b.width) * 0.5f + 1f;
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;   // the land, not the HUD
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            for (var i = 0; i < 4; i++) yield return null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            var dir = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Captures"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "world_overview.png"), tex.EncodeToPNG());
            Debug.Log($"[Overview] bounds {b} ortho {cam.orthographicSize}");
            Object.Destroy(tex);
            rt.Release();
        }
    }
}
