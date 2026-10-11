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
            yield return new WaitForSeconds(0.6f);
            yield return Capture.Take("mining_prototype");

            p.BackToTown();
            yield return Until(() => SandboxSceneController.Current != null, 10f, "back in town");
            Assert.IsTrue(GameRoot.Instance.Hud.gameObject.activeSelf);
        }
    }
}
