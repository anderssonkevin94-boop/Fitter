using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>The option-A digging prototype: two blocks tall, dig any neighbouring block, walk the tunnels you make.</summary>
    public sealed class MiningPrototypePlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-proto", Guid.NewGuid().ToString("N"));
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

        private static IEnumerator Until(Func<bool> done, float seconds, string what)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(done(), what);
        }

        [UnityTest]
        public IEnumerator DigDownTunnelAcrossAndGoBack()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            yield return Until(() => GameRoot.Instance != null && SandboxSceneController.Current != null, 10f, "town loaded");
            for (var i = 0; i < 3; i++) yield return null;

            // The signpost by the Mine opens the prototype.
            var sign = Object.FindAnyObjectByType<PrototypeSign>();
            Assert.NotNull(sign, "a signpost by the Mine");
            sign.OnInspected();
            yield return Until(() => MiningPrototype.Current != null, 10f, "prototype loaded");
            yield return null;
            var p = MiningPrototype.Current;
            var router = GameRoot.Instance.InputRouter;
            Assert.IsFalse(GameRoot.Instance.Hud.gameObject.activeSelf, "the prototype has its own controls");
            Assert.AreEqual(0, p.Feet, "starts on the surface");

            // Dig straight down four blocks: the supplier drops with each one.
            router.ScriptedMove = Vector2.down;
            p.ScriptedDig = true;
            yield return Until(() => p.Feet >= 4, 12f, "digs down and drops");
            // Then tunnel sideways (left on screen): two blocks high, so the supplier walks in.
            var startU = p.U;
            router.ScriptedMove = Vector2.left;
            yield return Until(() => p.U > startU + 3.5f, 20f, "tunnels along the face and walks into it");
            p.ScriptedDig = false;
            router.ScriptedMove = null;
            Assert.AreEqual(4, p.Feet, "a level tunnel: no falling, no climbing");
            Assert.GreaterOrEqual(p.DugCount, 4 + 6, "the shaft down and a 2-high tunnel");
            Assert.Greater(p.Tally.Values.Sum(), 0);
            Assert.IsFalse(p.Solid(Mathf.RoundToInt(p.U), p.Feet - 1) || p.Solid(Mathf.RoundToInt(p.U), p.Feet - 2), "the supplier stands in open tunnel");
            // Tap a block to select it, then hold Dig: that block (not the stick's aim) is mined.
            var col = Mathf.RoundToInt(p.U);
            var pick = new Vector2Int(col - 1, p.Feet - 1);   // the block beside the body, the other way along the tunnel
            if (!p.Solid(pick.x, pick.y)) pick = new Vector2Int(col, p.Feet);   // else the floor under the feet
            Assert.IsTrue(p.Solid(pick.x, pick.y));
            var face = p.Top(pick.x, pick.y) + new Vector3(0f, -0.3f, 0f);
            p.OnTapped(Camera.main.WorldToScreenPoint(face));
            Assert.AreEqual(pick, p.Selected, "a tap selects the block drawn there");
            yield return null;
            Assert.AreEqual(pick, p.Target, "and it becomes the block to dig");
            p.ScriptedDig = true;
            yield return Until(() => !p.Solid(pick.x, pick.y), 5f, "the selected block is dug");
            p.ScriptedDig = false;
            Assert.IsNull(p.Selected, "the selection clears once dug");
            var far = new Vector2Int(col + 6, 2);
            Assert.IsFalse(p.InReach(far), "blocks far away are out of reach");

            yield return new WaitForSeconds(0.6f);
            yield return Capture.Take("mining_prototype");

            p.BackToTown();
            yield return Until(() => SandboxSceneController.Current != null, 10f, "back in town");
            Assert.IsTrue(GameRoot.Instance.Hud.gameObject.activeSelf);
        }
    }
}
