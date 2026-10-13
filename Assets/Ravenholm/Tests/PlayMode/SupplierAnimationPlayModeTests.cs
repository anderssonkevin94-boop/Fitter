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
    /// <summary>The supplier's painted animations: walk views and mirroring, the carry pose holding the pile, mining, climbing.</summary>
    public sealed class SupplierAnimationPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-anim", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameRoot.Instance != null && GameRoot.Instance.InputRouter != null) GameRoot.Instance.InputRouter.ScriptedMove = null;
            if (GameRoot.Instance != null && GameRoot.Instance.Hud != null) GameRoot.Instance.Hud.ScriptedHold = false;
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
        public IEnumerator TheSupplierWalksCarriesMinesAndClimbs()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            yield return Until(() => GameRoot.Instance != null && SandboxSceneController.Current != null, 10f, () => "town loaded");
            for (var i = 0; i < 3; i++) yield return null;
            var player = SandboxSceneController.Current.Player;
            var anim = player.GetComponentInChildren<SupplierAnimator>();
            Assert.NotNull(anim, "the supplier has painted animations");
            var body = anim.GetComponent<Transform>().Find("Base").GetComponent<SpriteRenderer>();
            string Now() => $"clip {anim.Clip} frame {anim.Frame} back {anim.Back} mirrored {anim.Mirrored}";
            Assert.AreEqual("idle_front", anim.Clip, Now());

            // Walking up the screen shows his back; down and to the right, his front, mirrored.
            Stick(new Vector2(-0.6f, 0.8f));
            yield return Until(() => anim.Clip == "walk_back", 3f, Now);
            Assert.IsFalse(anim.Mirrored);
            Stick(new Vector2(0.8f, -0.6f));
            yield return Until(() => anim.Clip == "walk_front" && anim.Mirrored, 3f, Now);
            Assert.IsTrue(body.flipX, "mirrored for the right");
            var seen = anim.Frame;
            yield return Until(() => anim.Frame != seen, 2f, () => "the walk cycle plays");
            Stick(null);
            yield return Until(() => anim.Clip == "idle_front", 2f, Now);

            // Carrying: the forklift pose, with the pile on his forearms (in front of him, toward his facing).
            var sys = GameRoot.Instance.Services.Logistics.System;
            Assert.IsTrue(sys.TryCarry("resource.iron_ore"));
            yield return Until(() => anim.Clip == "carry_idle_front", 2f, Now);
            yield return null;
            Assert.IsTrue(anim.CarryAnchor.HasValue, "the carry frames know where the forearms are");
            var stack = player.Find("CarryStack");
            Assert.NotNull(stack);
            Assert.Greater(Mathf.Abs(stack.localPosition.x), 0.15f, "the pile sits out in front, on the arms");
            Assert.Less(stack.localPosition.y, 0.8f, "at arm height, not on his head");
            Stick(new Vector2(0.8f, -0.6f));
            yield return Until(() => anim.Clip == "carry_walk_front", 3f, Now);
            Stick(null);
            for (var i = 0; i < 9; i++) sys.TryCarry(i % 2 == 0 ? "resource.iron_ore" : "resource.coal");   // a big pile
            yield return new WaitForSeconds(0.8f);
            yield return Capture.Take("supplier_carry_front");
            Stick(new Vector2(-0.6f, 0.8f));
            yield return Until(() => anim.Clip == "carry_walk_back", 3f, Now);
            yield return new WaitForSeconds(0.3f);
            Stick(null);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual("carry_idle_back", anim.Clip, Now());
            yield return Capture.Take("supplier_carry_back");
            sys.DebugUnloadCarry();

            // In the Mine: digging into the quarry plays the pick swung down.
            var slab = MineSlabView.Current;
            var motor = player.GetComponent<PlayerMotor>();
            motor.Teleport(slab.Top(0f, 1f, 0f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.5f);
            GameRoot.Instance.Services.Sites.SpeedMultiplier = 20f;
            GameRoot.Instance.Hud.ScriptedHold = true;
            yield return Until(() => anim.Clip == "mine_down_front", 3f, Now);
            yield return Until(() => slab.PlayerInside && slab.Feet >= 3, 20f, () => $"digs down ({Now()}, feet {slab.Feet})");
            GameRoot.Instance.Hud.ScriptedHold = false;
            yield return Until(() => !slab.Falling, 3f, () => "lands");
            Stick(null);
            GameRoot.Instance.Services.Sites.SpeedMultiplier = 1f;
            yield return new WaitForSeconds(0.3f);
            yield return Capture.Take("supplier_animation");
        }
    }
}
