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
    /// <summary>The forest-mountains prototype: step up one block, walls of two, a gentle pass, digging through the ridge.</summary>
    public sealed class MountainPrototypePlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-mountain", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameRoot.Instance != null && GameRoot.Instance.InputRouter != null) GameRoot.Instance.InputRouter.ScriptedMove = null;
            if (GameRoot.Instance != null) Object.Destroy(GameRoot.Instance.gameObject);
            yield return null;
            yield return null;
            GameRoot.SaveDirectoryOverride = null;
            try { Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        private static IEnumerator Until(Func<bool> done, float seconds, Func<string> what)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(done(), what());
        }

        [UnityTest]
        public IEnumerator ClimbChopAndDigThroughTheRidge()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            yield return Until(() => GameRoot.Instance != null && SandboxSceneController.Current != null, 10f, () => "town loaded");
            for (var i = 0; i < 3; i++) yield return null;
            var sign = Array.Find(Object.FindObjectsByType<PrototypeSign>(FindObjectsSortMode.None), s => s.Title.Contains("mountains"));
            Assert.NotNull(sign, "a signpost by the forest gate");
            sign.OnInspected();
            yield return Until(() => MountainPrototype.Current != null, 10f, () => "prototype loaded");
            yield return null;
            var p = MountainPrototype.Current;
            Assert.Greater(p.DrawnBlocks, 100);
            Assert.AreEqual(1, p.Feet, "starts on the forest floor");

            // The pass climbs one block a step; the ridge elsewhere is walls of two or more.
            for (var x = 0; x < MountainPrototype.Size - 1; x++)
                Assert.LessOrEqual(Mathf.Abs(p.TopOf(x + 1, 4) - p.TopOf(x, 4)), 1, $"the pass at x {x}");
            var steep = false;
            for (var x = 0; x < MountainPrototype.Size - 1; x++) steep |= Mathf.Abs(p.TopOf(x + 1, 9) - p.TopOf(x, 9)) >= 2;
            Assert.IsTrue(steep, "the ridge has walls");

            // Walk toward the ridge (up-right on screen) with the button held: trees are chopped, walls dug, steps climbed.
            var router = GameRoot.Instance.InputRouter;
            router.ScriptedMove = new Vector2(2f, 1f).normalized;
            p.ScriptedWork = true;
            var highest = p.Feet;
            yield return Until(() =>
            {
                highest = Mathf.Max(highest, p.Feet);
                return p.Position.x > MountainPrototype.RidgeX + 4.5f;
            }, 90f, () => $"through the ridge (at {p.Position}, feet {p.Feet}, target {p.Target}, tree {p.TreeTarget}, clip {p.Clip})");
            p.ScriptedWork = false;
            router.ScriptedMove = null;
            Assert.Greater(p.DugCount, 0, "dug through rock");
            Assert.Greater(p.Tally.Count, 0);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(p.TopOf(p.Cell.x, p.Cell.y), p.Feet, "standing on the ground beyond");
            yield return Capture.Take("mountain_prototype");

            p.BackToTown();
            yield return Until(() => SandboxSceneController.Current != null, 10f, () => "back in town");
        }
    }
}
