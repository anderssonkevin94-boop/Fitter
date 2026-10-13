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
        public IEnumerator ABiggerPlotOfMountainsStumpsRegrowOrAreCleared()
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
            Assert.AreEqual(1, p.Feet, "starts on the forest floor");
            Assert.Greater(p.DrawnBlocks, 100);
            Assert.Less(p.DrawnBlocks, MountainPrototype.Size * MountainPrototype.Size, "only the area around the camera is drawn");

            // Mountains: peaks up to seven blocks, slopes of one (climbable) and of two or more (walls).
            int high = 0, gentle = 0, walls = 0;
            for (var x = 0; x < MountainPrototype.Size - 1; x++)
            for (var y = 0; y < MountainPrototype.Size; y++)
            {
                if (p.TopOf(x, y) >= 6) high++;
                var step = Mathf.Abs(p.TopOf(x + 1, y) - p.TopOf(x, y));
                if (step == 1) gentle++;
                if (step >= 2) walls++;
            }
            Assert.Greater(high, 40, "real mountains");
            Assert.Greater(gentle, 50, "slopes you can climb");
            Assert.Greater(walls, 50, "and walls to dig through or go around");

            // Stumps: one left standing grows back; one hit again is cleared and never grows back.
            MountainPrototype.RegrowSeconds = 1.5f;
            Vector2Int tree = default, stand = default, face = default;
            var found = false;
            for (var x = 2; x < MountainPrototype.Size - 2 && !found; x++)
            for (var y = 2; y < MountainPrototype.Size - 2 && !found; y++)
            {
                if (!p.HasTree(x, y)) continue;
                foreach (var d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                {
                    var s = new Vector2Int(x, y) - d;
                    if (p.HasTree(s.x, s.y) || p.HasStump(s.x, s.y) || p.TopOf(s.x, s.y) != p.TopOf(x, y)) continue;
                    tree = new Vector2Int(x, y); stand = s; face = d; found = true;
                    break;
                }
            }
            Assert.IsTrue(found, "a tree to stand beside");
            p.PutAt(stand, face);
            p.ScriptedWork = true;
            yield return Until(() => !p.HasTree(tree.x, tree.y), 10f, () => "the tree is felled");
            p.ScriptedWork = false;
            Assert.IsTrue(p.HasStump(tree.x, tree.y), "leaving a stump");
            yield return Until(() => p.HasTree(tree.x, tree.y), 5f, () => "the stump grows back into a tree");
            p.ScriptedWork = true;
            yield return Until(() => p.HasStump(tree.x, tree.y), 10f, () => "felled again");
            yield return null;
            Assert.AreEqual(tree, p.StumpTarget, "the button now offers to clear the stump");
            yield return Until(() => p.IsCleared(tree.x, tree.y), 5f, () => "hit again, the stump is pulled out");
            p.ScriptedWork = false;
            yield return new WaitForSeconds(2.5f);
            Assert.IsFalse(p.HasTree(tree.x, tree.y) || p.HasStump(tree.x, tree.y), "a cleared stump never grows back");
            MountainPrototype.RegrowSeconds = 60f;

            // A long walk across the plot with the button held: trees chopped, walls dug, slopes climbed.
            p.PutAt(MountainPrototype.StartCell, Vector2Int.right);
            var router = GameRoot.Instance.InputRouter;
            router.ScriptedMove = new Vector2(2f, 1f).normalized;
            p.ScriptedWork = true;
            yield return Until(() => p.Position.x > MountainPrototype.StartCell.x + 24f, 150f,
                () => $"across the plot (at {p.Position}, feet {p.Feet}, target {p.Target}, tree {p.TreeTarget}, clip {p.Clip})");
            p.ScriptedWork = false;
            router.ScriptedMove = null;
            Assert.Greater(p.Tally.Count, 0);
            yield return new WaitForSeconds(0.5f);
            yield return Capture.Take("mountain_prototype");

            p.BackToTown();
            yield return Until(() => SandboxSceneController.Current != null, 10f, () => "back in town");
        }
    }
}
