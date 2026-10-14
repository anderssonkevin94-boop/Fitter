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
    /// The game-drawn outline: the same thickness in screen pixels on sprites of any resolution or size, only around an
    /// object's outer silhouette (no line where its own pieces overlap), off for sprites not switched on.
    /// </summary>
    public sealed class OutlinePlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-outline", Guid.NewGuid().ToString("N"));
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

        /// <summary>A flat white square with a transparent margin (outline-free art), full-rect.</summary>
        private static Sprite Square(int size, int margin, float ppu)
        {
            var n = size + 2 * margin;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                px[y * n + x] = x >= margin && x < n - margin && y >= margin && y < n - margin ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
        }

        private static SpriteOutline MakeObject(Material fill, string name, Vector2 at, float scale, bool outlineAll, params (Sprite sprite, Vector2 offset)[] pieces)
        {
            var root = new GameObject(name);
            root.transform.position = at;
            root.transform.localScale = Vector3.one * scale;
            var o = root.AddComponent<SpriteOutline>();
            o.OutlineAll = outlineAll;
            var i = 0;
            foreach (var (sprite, offset) in pieces)
            {
                var go = new GameObject($"Piece{i}");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = offset;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                if (fill != null) sr.sharedMaterial = fill;
                sr.sortingOrder = i++;
            }
            return o;
        }

        private static bool IsLine(Color c) => c.r < 0.35f && c.g < 0.35f && c.b < 0.35f;
        private static bool IsBackground(Color c) => c.g > 0.6f && c.r < 0.4f;

        /// <summary>Line pixels crossed walking right from a screen point until the background.</summary>
        private static int LineRun(Texture2D tex, Vector2 from)
        {
            int x = Mathf.RoundToInt(from.x), y = Mathf.RoundToInt(from.y), n = 0;
            for (; x < tex.width && !IsBackground(tex.GetPixel(x, y)); x++) if (IsLine(tex.GetPixel(x, y))) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator TheOutlineHasOneThicknessAndOnlyFollowsTheSilhouette()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.NotNull(SandboxSceneController.Current, "town loaded");
            var settings = GameRoot.Instance.Config.SandboxSettings.outlines;
            Assert.NotNull(settings, "outline settings are wired");
            Assert.NotNull(settings.material, "with the silhouette material");

            // Far from the town, on a plain background: a small low-res square, a big hi-res square, two overlapping
            // squares as one object, and a square whose art isn't switched on.
            Material unlit = null;   // the town's unlit sprite material: plain colours, whatever the lights
            foreach (var r in UnityEngine.Object.FindObjectsByType<SpriteRenderer>())
                if (r.sharedMaterial != null && r.sharedMaterial.name.Contains("Unlit")) { unlit = r.sharedMaterial; break; }
            var at = new Vector2(1000f, 1000f);
            var low = MakeObject(unlit, "LowRes", at + new Vector2(-1.2f, 1.5f), 1f, true, (Square(96, 16, 128), Vector2.zero));      // ~2 screen px a texel
            var high = MakeObject(unlit, "HighRes", at + new Vector2(0.9f, 1.5f), 2f, true, (Square(320, 64, 1024), Vector2.zero));  // ~0.5 px a texel, scaled 2x
            var pair = MakeObject(unlit, "Pair", at + new Vector2(-1.2f, -2f), 1f, true, (Square(160, 32, 256), Vector2.zero), (Square(160, 32, 256), new Vector2(0.4f, 0f)));
            var off = MakeObject(unlit, "NotSwitchedOn", at + new Vector2(1.2f, -2f), 1f, false, (Square(160, 32, 256), Vector2.zero));

            var cam = SandboxSceneController.Current.WorldCamera;
            var follow = cam.GetComponent<FollowCamera>();
            if (follow != null) follow.enabled = false;
            cam.transform.position = new Vector3(at.x, at.y, cam.transform.position.z);
            cam.orthographicSize = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.8f, 0.2f, 1f);
            const int w = 1206, h = 2622;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            for (var i = 0; i < 4; i++) yield return null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            // Screen points while the camera still draws into the capture (its aspect, not the window's).
            Vector2 ToScreen(Vector2 world) => cam.WorldToScreenPoint(world);
            Vector2 pLow = ToScreen(low.transform.position), pHigh = ToScreen(high.transform.position);
            Vector2 pPair = ToScreen(pair.transform.position), pOff = ToScreen(off.transform.position);
            cam.targetTexture = null;
            var dir = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Captures"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "outline_test.png"), tex.EncodeToPNG());

            var expected = settings.PixelsFor(h);   // the thickness follows the camera's render height
            Assert.Greater(expected, 1f);
            var lowRun = LineRun(tex, pLow);
            var highRun = LineRun(tex, pHigh);
            Assert.AreEqual(expected, lowRun, 1.5f, "a small low-resolution sprite gets the set thickness");
            Assert.AreEqual(expected, highRun, 1.5f, "a big high-resolution sprite gets the same thickness");
            Assert.LessOrEqual(Mathf.Abs(lowRun - highRun), 1, "one thickness on every asset");

            // Across the pair's overlap: one line at the far edge only, none between the two pieces.
            var pairRun = LineRun(tex, pPair);
            Assert.AreEqual(expected, pairRun, 1.5f, "no line where an object's own pieces overlap");
            Assert.AreEqual(2, pair.ActiveOutlines);

            Assert.AreEqual(0, off.ActiveOutlines, "art that isn't switched on keeps its own look");
            Assert.AreEqual(0, LineRun(tex, pOff), "no game outline on it");

            // The player has the component, and nothing on him is switched on yet (his art still has painted lines).
            var player = SandboxSceneController.Current.Player.GetComponent<SpriteOutline>();
            Assert.NotNull(player, "the supplier is ready for outline-free art");
            Assert.AreEqual(0, player.ActiveOutlines);
            Object.Destroy(tex);
            rt.Release();
        }
    }
}
