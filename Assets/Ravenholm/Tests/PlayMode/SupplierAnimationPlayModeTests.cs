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
    /// <summary>The supplier's animations (the dwarf): eight walking directions, the pile on his head, a procedural swing.</summary>
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
            string Now() => $"clip {anim.Clip} (asked {anim.Requested}) frame {anim.Frame} dir {anim.Dir}";
            Assert.AreEqual("idle_S", anim.Clip, Now());

            // Eight painted directions, never mirrored: up the screen is N, down and to the right SE.
            Stick(new Vector2(-0.6f, 0.8f));
            yield return Until(() => anim.Clip == "walk_N", 3f, Now);
            Stick(new Vector2(0.8f, -0.6f));
            yield return Until(() => anim.Clip == "walk_SE", 3f, Now);
            Assert.IsFalse(body.flipX, "the dwarf is painted for every direction");
            var seen = anim.Frame;
            yield return Until(() => anim.Frame != seen, 2f, () => "the walk cycle plays");
            Stick(new Vector2(-1f, 0f));
            yield return Until(() => anim.Clip == "walk_W", 3f, Now);
            yield return Capture.Take("dwarf_walk_west");
            Stick(new Vector2(0.8f, -0.6f));
            yield return Until(() => anim.Clip == "walk_SE", 3f, Now);
            Stick(null);
            yield return Until(() => anim.Clip == "idle_SE", 2f, Now);

            // Carrying: no carry pose painted yet, so the pile rides on his head, just above it.
            var sys = GameRoot.Instance.Services.Logistics.System;
            Assert.IsTrue(sys.TryCarry("resource.iron_ore"));
            yield return Until(() => anim.Requested == "carry_idle_front", 2f, Now);
            yield return null;
            Assert.IsFalse(anim.CarryAnchor.HasValue, "no painted forearms to hold it");
            var stack = player.Find("CarryStack");
            Assert.NotNull(stack);
            Assert.Less(Mathf.Abs(stack.localPosition.x), 0.05f, "centred over him");
            Assert.That(stack.localPosition.y, Is.InRange(anim.HeadTop - 0.06f, anim.HeadTop + 0.03f), "on top of his head");
            Stick(new Vector2(0.8f, -0.6f));
            yield return Until(() => anim.Requested == "carry_walk_front" && anim.Clip == "walk_SE", 3f, Now);
            Stick(null);
            for (var i = 0; i < 9; i++) sys.TryCarry(i % 2 == 0 ? "resource.iron_ore" : "resource.coal");   // a big pile
            yield return new WaitForSeconds(0.8f);
            yield return Capture.Take("supplier_carry_front");
            sys.DebugUnloadCarry();

            // In the Mine: digging into the quarry plays the pick swung down.
            var slab = MineSlabView.Current;
            var motor = player.GetComponent<PlayerMotor>();
            motor.Teleport(slab.Top(0f, 1f, 0f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.5f);
            GameRoot.Instance.Services.Sites.SpeedMultiplier = 20f;
            GameRoot.Instance.Hud.ScriptedHold = true;
            yield return Until(() => anim.Requested == "mine_down_front" && anim.Clip.StartsWith("idle_"), 3f, Now);
            var leaned = false;
            var t0 = Time.time;
            while (Time.time - t0 < 1f && !leaned)
            {
                leaned = Mathf.Abs(Mathf.DeltaAngle(0f, body.transform.localEulerAngles.z)) > 2f || Mathf.Abs(body.transform.localScale.y - 1f) > 0.03f;
                yield return null;
            }
            Assert.IsTrue(leaned, "the standing pose swings the pick (procedurally)");
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
