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
    /// <summary>The cliff-face prototype: one flat slice, dig down with a ladder left behind, climb out, tunnel left.</summary>
    public sealed class CliffPrototypePlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-cliff", Guid.NewGuid().ToString("N"));
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

        private static void Stick(Vector2? v) => GameRoot.Instance.InputRouter.ScriptedMove = v;

        [UnityTest]
        public IEnumerator DigDownClimbTheLadderOutAndTunnelLeft()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            yield return Until(() => GameRoot.Instance != null && SandboxSceneController.Current != null, 10f, () => "town loaded");
            for (var i = 0; i < 3; i++) yield return null;

            var sign = Object.FindAnyObjectByType<PrototypeSign>();
            Assert.NotNull(sign, "a signpost by the Mine");
            sign.OnInspected();
            yield return Until(() => CliffPrototype.Current != null, 10f, () => "prototype loaded");
            yield return null;
            var p = CliffPrototype.Current;
            string Where() => $"at u {p.U:0.00}, feet {p.Feet}, target {p.Target}, ladder {p.OnLadder}";
            Assert.AreEqual(0, p.Feet, "starts on the surface");
            StringAssert.Contains("surface", p.DepthText);
            var shaft = p.Column;

            // Down + Dig digs straight down; from two blocks deep the shaft has a ladder.
            Stick(Vector2.down);
            p.ScriptedDig = true;
            yield return Until(() => p.Feet >= 4, 30f, () => "digs straight down " + Where());
            p.ScriptedDig = false;
            Stick(null);
            Assert.AreEqual(shaft, p.Column, "straight down, no drifting");
            for (var r = 0; r < 4; r++) Assert.IsTrue(p.Ladder(shaft, r), $"a ladder in the shaft at row {r}");
            yield return null;
            Assert.IsTrue(p.LadderDrawn(shaft, 0) && p.LadderDrawn(shaft, 3), "and it is drawn");
            StringAssert.Contains("Depth 4", p.DepthText);

            // Up climbs the ladder to the top; then walk off it onto the ground (screen right).
            Stick(Vector2.up);
            yield return Until(() => p.Feet == 0, 10f, () => "climbs the ladder out " + Where());
            Stick(Vector2.right);
            yield return Until(() => p.Column == shaft - 1, 5f, () => "steps off the ladder onto the ground " + Where());
            Stick(null);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(0, p.Feet, "standing on the ground");

            // Back onto the ladder's top and down it.
            Stick(Vector2.left);
            yield return Until(() => p.Column == shaft, 5f, () => "back onto the ladder " + Where());
            Stick(null);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(0, p.Feet, "the ladder's top holds the supplier");
            Stick(Vector2.down);
            yield return Until(() => p.Feet == 4, 10f, () => "climbs down " + Where());
            Stick(null);

            // Left + Dig tunnels along the cliff, two blocks high, on the same level.
            var startU = p.U;
            Stick(Vector2.left);
            p.ScriptedDig = true;
            yield return Until(() => p.U > startU + 3.5f, 30f, () => "tunnels left " + Where());
            p.ScriptedDig = false;
            Stick(null);
            Assert.AreEqual(4, p.Feet, "a level tunnel");
            Assert.IsFalse(p.Solid(p.Column, 3) || p.Solid(p.Column, 2), "two blocks high");
            Assert.IsTrue(p.Solid(p.Column, 4), "with its floor");
            Assert.Greater(p.Tally.Count, 0);
            Assert.Greater(p.OutlinedBlocks, 0, "reachable blocks are outlined");

            // Depth: the block over the head (nearer the camera) covers the supplier; the floor is behind them.
            yield return null;
            Assert.Less(p.DrawnOrder(p.Column, 4), p.PlayerOrder);
            yield return new WaitForSeconds(0.6f);
            yield return Capture.Take("cliff_prototype");

            p.BackToTown();
            yield return Until(() => SandboxSceneController.Current != null, 10f, () => "back in town");
            Assert.IsTrue(GameRoot.Instance.Hud.gameObject.activeSelf);
        }
    }
}
