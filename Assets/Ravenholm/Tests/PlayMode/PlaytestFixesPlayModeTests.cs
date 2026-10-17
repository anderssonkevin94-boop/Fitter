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
    /// The playtester's findings, fixed (2026-10-17): trees in front of the supplier fade as buildings do; the camp's
    /// message fits its box below the debug banner.
    /// </summary>
    public sealed class PlaytestFixesPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-fixes", Guid.NewGuid().ToString("N"));
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

        [UnityTest]
        public IEnumerator TreesFadeInFrontOfTheSupplierAndTheMessageFitsItsBox()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            for (var i = 0; i < 3; i++) yield return null;
            var root = GameRoot.Instance;
            root.Hud.Tips.HideAll();
            var forest = ForestGridView.Current;
            var sim = root.Services.Sites.Get("site.forest");
            var tree = CampPlayModeTests.TreeAhead(sim, 10);
            Assert.IsTrue(forest.CellOf(tree.x, tree.y, out var cell));
            var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            // Just behind the tree (higher on the screen), where its crown covers him.
            motor.Teleport((Vector2)forest.RendererAt(cell).transform.position + new Vector2(0f, 0.3f));
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(forest.TreeFaded(cell), "the tree in front of him fades");
            yield return Capture.Take("tree_fade");
            motor.Teleport((Vector2)forest.RendererAt(cell).transform.position + new Vector2(0f, -1.5f));   // in front of it now
            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(forest.TreeFaded(cell), "and comes back when he steps out");

            // The long camp message: inside its box, below the banner.
            root.Hud.ShowToast("Camp made at Hollow Wood. Set your stations up from their kits (Build).");
            yield return new WaitForSeconds(0.3f);
            var toast = GameObject.Find("Toast").GetComponent<RectTransform>();
            var text = toast.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            Assert.LessOrEqual(text.preferredHeight, toast.rect.height, "the text fits inside its box");
            var banner = GameObject.Find("PhaseBanner");
            if (banner != null && banner.activeInHierarchy)
            {
                var b = banner.GetComponent<RectTransform>();
                var bc = new Vector3[4];
                var tc = new Vector3[4];
                b.GetWorldCorners(bc);
                toast.GetWorldCorners(tc);
                Assert.LessOrEqual(tc[1].y, bc[0].y + 0.5f, "the message starts below the banner");
            }
            yield return Capture.Take("toast_fits");
        }
    }
}
