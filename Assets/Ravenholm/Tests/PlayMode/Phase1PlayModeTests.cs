using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Domain.Logistics;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Ravenholm.Tests
{
    /// <summary>Phase 1 acceptance in the real DebugSandbox scene against a temporary save folder.</summary>
    public sealed class Phase1PlayModeTests
    {
        private string _dir;

        /// <summary>Where to stand to use a container or receiver, read from the scene (layouts change; tests should not care).</summary>
        public static Vector2 Spot(string id)
        {
            if (ReceiverView.TryGet(id, out var r)) return r.transform.position;
            if (WorldContainerView.TryGet(id, out var c)) return (Vector2)c.transform.position + new Vector2(0f, -0.4f);
            Assert.Fail("no container or receiver " + id);
            return Vector2.zero;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-phase1", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return KillRoot();
            GameRoot.SaveDirectoryOverride = null;
            SafeAreaFitter.NormalizedOverride = null;
            FollowCamera.ViewWidthIndex = -1;
            try { Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        // ------------------------------------------------------------------ helpers

        private static IEnumerator Boot()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            yield return null;   // the previous test's sandbox is still active during the load call's frame
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.NotNull(SandboxSceneController.Current, "DebugSandbox did not finish loading");
            // Phase 1 tests exercise hauling only: freeze the Phase 2 workers so they cannot change pile counts.
            GameRoot.Instance.Services.Sites.SpeedMultiplier = 0f;
            for (var i = 0; i < 3; i++) yield return null;
        }

        /// <summary>Test fixture: the Phase 1 contents of both staging piles (25 and 26 units) and the Forge pad (5 items).</summary>
        private static void SeedStaging()
        {
            if (Sys.State.Containers["container.mine_staging"].UnitCount > 0) return;
            Sys.CreateMaterialsInContainer("container.mine_staging", "resource.iron_ore", 18);
            Sys.CreateMaterialsInContainer("container.mine_staging", "resource.copper_ore", 6);
            Sys.CreateMaterialsInContainer("container.mine_staging", "resource.gem", 1);
            Sys.CreateMaterialsInContainer("container.forest_staging", "resource.cedar_log", 16);
            Sys.CreateMaterialsInContainer("container.forest_staging", "resource.maple_log", 4);
            Sys.CreateMaterialsInContainer("container.forest_staging", "resource.herb", 6);
            foreach (var item in new[] { "item.iron_sword", "item.iron_sword", "item.iron_dagger", "item.riveted_coat", "item.riveted_coat" })
                Sys.CreateItemInContainer("container.forge_pad", item);
        }

        private static IEnumerator KillRoot()
        {
            if (GameRoot.Instance != null)
            {
                GameRoot.Instance.Services?.Scheduler.Flush("test restart");
                Object.Destroy(GameRoot.Instance.gameObject);
            }
            yield return null;
            yield return null;
        }

        private static LogisticsSystem Sys => GameRoot.Instance.Services.Logistics.System;
        private static PlayerMotor Motor => SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();

        private static IEnumerator StandAt(Vector2 p, float seconds)
        {
            Motor.Teleport(p);
            var end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        private static int Shared(string id) => Sys.State.SharedMaterials.TryGetValue(id, out var q) ? q : 0;

        // ------------------------------------------------------------------ tests

        [UnityTest]
        public IEnumerator CannotWalkThroughBuildingsOrLeaveTown()
        {
            yield return Boot();
            var mask = LayerMask.GetMask(PlayerMotor.BlockerLayerName);
            var radius = GameRoot.Instance.Config.Balance.playerFootprintRadius;
            var cases = new[]
            {
                (from: (Vector2)GameObject.Find("Smelter").transform.position + new Vector2(0f, -1.7f), to: (Vector2)GameObject.Find("Smelter").transform.position, name: "Smelter"),
                (from: new Vector2(-3.5f, -5.6f), to: new Vector2(-5.4f, -3.8f), name: "Forge"),
                (from: new Vector2(-2.9f, -3.2f), to: new Vector2(-2.9f, -1.7f), name: "Apothecary"),
                (from: (Vector2)ShopView.Current.Wagon.position + new Vector2(0f, 1.1f), to: (Vector2)ShopView.Current.Wagon.position, name: "The wagon"),
                (from: (Vector2)GameObject.Find("Lumbermill").transform.position + new Vector2(1.9f, -1.2f), to: (Vector2)GameObject.Find("Lumbermill").transform.position, name: "Lumbermill"),
            };
            foreach (var c in cases)
            {
                var p = c.from;
                Assert.IsNull(Physics2D.OverlapCircle(p, radius, mask), $"{c.name}: start point must be free");
                for (var i = 0; i < 300; i++) p = PlayerMotor.Slide(p, (c.to - p).normalized * 0.06f, radius, mask);
                Assert.IsNull(Physics2D.OverlapCircle(p, radius * 0.9f, mask), $"{c.name}: player ended inside a footprint at {p}");
                Assert.Greater(Vector2.Distance(p, c.to), 0.3f, $"{c.name}: reached the building centre");
            }

            var q = new Vector2(10f, 0f);
            for (var i = 0; i < 200; i++) q = PlayerMotor.Slide(q, Vector2.right * 0.06f, radius, mask);
            Assert.LessOrEqual(q.x, GameRoot.Instance.Config.SandboxSettings.WorldBounds.xMax);

            // Sliding: pushing diagonally into a wall still makes progress along it.
            // (the walk-in wagon's back wall, 2026-10-16: from behind it, pushing into it at an angle).
            var cart = WalkInCart.Current;
            var wa = (Vector2)cart.transform.position + cart.Footprint[0];
            var wb = (Vector2)cart.transform.position + cart.Footprint[1];
            var along = (wb - wa).normalized;
            var outward = new Vector2(-along.y, along.x);   // the back wall's far side (up the screen)
            if (outward.y < 0f) outward = -outward;
            var s = Vector2.Lerp(wa, wb, 0.3f) + outward * (radius * 2.5f);
            var s0 = s;
            for (var i = 0; i < 40; i++) s = PlayerMotor.Slide(s, (along - outward).normalized * 0.06f, radius, mask);
            Assert.Greater(Vector2.Dot(s - s0, along), 0.5f, "slides along the wagon's back wall");
        }

        [UnityTest]
        public IEnumerator HaulLoopFillsCarryUsesCorrectReceiversAndConserves()
        {
            yield return Boot();
            SeedStaging();
            Assert.AreEqual(25, Sys.State.Containers["container.mine_staging"].UnitCount, "fixture seeded");

            yield return StandAt(Spot("container.mine_staging"), 2.0f);   // mine staging
            Assert.AreEqual(12, Sys.CarryCount, "fills to capacity");
            Assert.AreEqual(13, Sys.State.Containers["container.mine_staging"].UnitCount);
            Assert.AreEqual("Carry full", GameRoot.Instance.Hud.ToastText);
            yield return Capture.Take("phase1_full_at_mine");

            yield return StandAt(Spot("receiver.smelter"), 1.6f);  // smelter receiver
            Assert.AreEqual(0, Sys.CarryCount, "smelter accepts ore and gems");
            Assert.AreEqual(12, Shared("resource.iron_ore") + Shared("resource.copper_ore") + Shared("resource.gem"));

            yield return StandAt(new Vector2(-4.3f, -4.0f), 1.0f);   // forge output pad
            Assert.AreEqual(5, Sys.CarryCount);
            Assert.IsTrue(Sys.State.Carry.All(u => u.Kind == UnitKind.Item));

            yield return StandAt(Spot("receiver.smelter"), 1.0f);  // wrong receiver
            Assert.AreEqual(5, Sys.CarryCount, "smelter refuses finished goods; they stay carried");

            yield return StandAt(Spot("receiver.store"), 1.0f);  // store receiving strip
            yield return Capture.Take("phase1_store_receiving");
            Assert.AreEqual(0, Sys.CarryCount);
            Assert.AreEqual(5, Sys.State.StoreItems.Count);

            // Forest staging holds 16 cedar, 6 herbs, 4 maple; pickup order is by stable ID, so the
            // first trip is all cedar and the second trip mixes in the herbs.
            yield return StandAt(Spot("container.forest_staging"), 2.0f);
            yield return StandAt(Spot("receiver.lumbermill"), 1.6f);
            Assert.AreEqual(0, Sys.CarryCount);
            yield return StandAt(Spot("container.forest_staging"), 2.0f);
            Assert.AreEqual(6, Sys.State.Carry.Count(u => u.Id == "resource.herb"));
            yield return StandAt(Spot("receiver.lumbermill"), 1.6f);   // lumbermill takes logs only
            Assert.AreEqual(6, Sys.CarryCount);
            Assert.IsTrue(Sys.State.Carry.All(u => u.Id == "resource.herb"), "herbs remain for the apothecary");
            yield return StandAt(new Vector2(-2.15f, -2.45f), 1.0f);
            Assert.AreEqual(0, Sys.CarryCount);

            CollectionAssert.IsEmpty(Sys.Audit());
        }

        [UnityTest]
        public IEnumerator RestartPreservesCarryAndPilesWithoutDuplicates()
        {
            yield return Boot();
            SeedStaging();
            yield return StandAt(Spot("container.mine_staging"), 0.08f * 7 + 0.05f);
            var carried = Sys.CarryCount;
            Assert.That(carried, Is.InRange(5, 9));
            var carry = Sys.State.Carry.ToArray();
            var pile = Sys.State.Containers["container.mine_staging"].UnitCount;
            Motor.Teleport(new Vector2(0f, -1f));   // step away so nothing more transfers

            yield return KillRoot();
            yield return Boot();
            CollectionAssert.AreEqual(carry, Sys.State.Carry.ToArray(), "carry restored exactly");
            Assert.AreEqual(pile, Sys.State.Containers["container.mine_staging"].UnitCount, "pile not reseeded");
            Assert.AreEqual(25, carried + pile);
            CollectionAssert.IsEmpty(Sys.Audit());

            SandboxSceneController.Current.Unstuck();
            Assert.AreEqual(carried, Sys.CarryCount, "unstuck keeps carried ownership");
        }

        [UnityTest]
        public IEnumerator TouchJoystickResetsOnModalCancelAndRelease()
        {
            yield return Boot();
            var router = GameRoot.Instance.InputRouter;
            // Headless editors have no focused Game view; route all device input to the game for this test.
            var previousBehaviour = InputSystem.settings.editorInputBehaviorInPlayMode;
            var previousBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var screen = InputSystem.AddDevice<Touchscreen>();
            try
            {
                var start = new Vector2(Screen.width * 0.5f, Screen.height * 0.4f);
                yield return Touch(screen, 1, TouchPhase.Began, start);
                yield return Touch(screen, 1, TouchPhase.Moved, start + new Vector2(0, 100));
                Assert.Greater(router.MoveVector.y, 0.9f, "drag up moves up the screen");
                Assert.IsTrue(router.JoystickVisible);
                yield return Capture.Take("phase1_joystick");

                var bell = Object.FindAnyObjectByType<BellInspectable>();
                GameRoot.Instance.Hud.ShowInfo(bell);   // modal opens mid-drag
                yield return null;
                Assert.AreEqual(Vector2.zero, router.MoveVector, "modal zeroes movement");

                GameRoot.Instance.Hud.HideInfo();
                yield return Touch(screen, 1, TouchPhase.Moved, start + new Vector2(0, 140));
                Assert.AreEqual(Vector2.zero, router.MoveVector, "a touch held across a modal stays ignored");
                yield return Touch(screen, 1, TouchPhase.Ended, start + new Vector2(0, 140));

                yield return Touch(screen, 2, TouchPhase.Began, start);
                yield return Touch(screen, 2, TouchPhase.Moved, start + new Vector2(100, 0));
                Assert.Greater(router.MoveVector.x, 0.9f, "a fresh touch works again");
                yield return Touch(screen, 2, TouchPhase.Canceled, start + new Vector2(100, 0));
                Assert.AreEqual(Vector2.zero, router.MoveVector, "cancel resets");
                Assert.IsFalse(router.JoystickVisible);

                // Touches that start in the top of the screen never drive the joystick.
                var high = new Vector2(Screen.width * 0.5f, Screen.height * 0.9f);
                yield return Touch(screen, 3, TouchPhase.Began, high);
                yield return Touch(screen, 3, TouchPhase.Moved, high + new Vector2(0, -100));
                Assert.AreEqual(Vector2.zero, router.MoveVector, "outside the lower movement area");
                yield return Touch(screen, 3, TouchPhase.Ended, high + new Vector2(0, -100));
            }
            finally
            {
                InputSystem.RemoveDevice(screen);
                InputSystem.settings.editorInputBehaviorInPlayMode = previousBehaviour;
                InputSystem.settings.backgroundBehavior = previousBackground;
            }
        }

        private static IEnumerator Touch(Touchscreen screen, int id, TouchPhase phase, Vector2 pos)
        {
            var state = new TouchState { touchId = id, phase = phase, position = pos };
            if (phase == TouchPhase.Began) state.startPosition = pos;
            InputSystem.QueueStateEvent(screen, state);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator BuildingFadesWhenPlayerIsBehindIt()
        {
            yield return Boot();
            // Behind a station (the shop is the wagon's open carpet now: nothing of it hides anyone): the station fades.
            var smelter = GameObject.Find("Smelter");
            var wall = smelter.GetComponent<SpriteRenderer>();
            var edge = (Vector2)smelter.transform.position;
            yield return StandAt(edge + new Vector2(0f, 0.9f), 0.6f);
            Assert.Less(wall.color.a, 0.6f);
            yield return Capture.Take("phase1_behind_store");
            yield return StandAt(new Vector2(0f, 0f), 0.6f);
            Assert.AreEqual(1f, wall.color.a, 1e-3f);
        }
    }

    /// <summary>Renders the sandbox camera (with HUD) to a PNG in &lt;project&gt;/Captures.</summary>
    public static class Capture
    {
        public static IEnumerator Take(string name, int w = 1206, int h = 2622)
        {
            SafeAreaFitter.NormalizedOverride = new Rect(0f, 102f / 2622f, 1f, 1f - (186f + 102f) / 2622f);
            var cam = SandboxSceneController.Current != null ? SandboxSceneController.Current.WorldCamera : Camera.main;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            for (var i = 0; i < 3; i++) yield return null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            var dir = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Captures"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
            SafeAreaFitter.NormalizedOverride = null;
        }
    }
}
