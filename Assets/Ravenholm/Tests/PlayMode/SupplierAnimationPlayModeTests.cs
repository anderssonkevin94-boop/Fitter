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
            Stick(new Vector2(0f, 1f));
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

        /// <summary>
        /// Every direction walks with its own painted clip at the painted planted-feet speed (times the playback rate),
        /// measured on the game's floor; diagonals face diagonally; turning keeps the step's phase; stopping keeps the facing.
        /// </summary>
        [UnityTest]
        public IEnumerator TheDwarfWalksAllEightDirectionsWithPlantedFeet()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            yield return Until(() => GameRoot.Instance != null && SandboxSceneController.Current != null, 10f, () => "town loaded");
            for (var i = 0; i < 3; i++) yield return null;
            var player = SandboxSceneController.Current.Player;
            var motor = player.GetComponent<PlayerMotor>();
            var anim = player.GetComponentInChildren<SupplierAnimator>();
            var set = anim.Set;
            Assert.AreEqual(2f, set.walkPlaybackRate, "Kevin's choice: twice the painted speed");
            var start = (Vector2)player.position;
            string[] dirs = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
            for (var k = 0; k < 8; k++)
            {
                motor.Teleport(start);
                yield return new WaitForSeconds(0.2f);
                var a = k * Mathf.PI / 4f;
                var stick = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Stick(stick);
                yield return Until(() => anim.Clip == "walk_" + dirs[k], 2f, () => $"walks {dirs[k]} (clip {anim.Clip})");
                yield return new WaitForSeconds(0.1f);
                var p0 = (Vector2)player.position;
                var t0 = Time.time;
                yield return new WaitForSeconds(0.45f);
                var v = ((Vector2)player.position - p0) / (Time.time - t0);
                var clip = set.Get("walk_" + dirs[k]);
                var planted = clip.velocity * anim.PlaybackRate;    // the ground the painted feet cover at this playback
                Assert.Less((v - planted).magnitude, 0.05f * planted.magnitude + 0.01f,
                    $"{dirs[k]}: feet planted (moves {v}, painted {planted} at {anim.PlaybackRate:F2}x)");
                Assert.AreEqual(2f, anim.PlaybackRate, 0.1f, $"{dirs[k]}: full stick plays at twice the painted timing");
                Assert.IsFalse(anim.GetComponentInChildren<SpriteRenderer>().flipX, "never mirrored");
                if (dirs[k] == "SE") yield return Capture.Take("dwarf_walk_SE");
            }

            // Turning while walking keeps the step's phase.
            motor.Teleport(start);
            yield return new WaitForSeconds(0.2f);
            Stick(Vector2.right);
            yield return Until(() => anim.Clip == "walk_E" && anim.Frame == 2, 2f, () => $"mid-step ({anim.Clip} {anim.Frame})");
            Stick(new Vector2(1f, 1f).normalized);
            yield return Until(() => anim.Clip == "walk_NE", 1f, () => $"turns to NE ({anim.Clip})");
            Assert.That(anim.Frame, Is.InRange(2, 3), "the step carries on from where it was");

            // A half push walks at half speed with half-speed steps (still planted).
            Stick(new Vector2(1f, 1f).normalized * 0.5f);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(1f, anim.PlaybackRate, 0.15f, "half the playback");

            // Stopping stands, facing the way it walked.
            Stick(null);
            yield return Until(() => anim.Clip == "idle_NE", 1f, () => $"stands facing NE ({anim.Clip})");
            Assert.AreEqual(0f, anim.GetComponentInChildren<SpriteRenderer>().transform.localEulerAngles.z, 0.01f, "no added rotation");
        }
    }
}
