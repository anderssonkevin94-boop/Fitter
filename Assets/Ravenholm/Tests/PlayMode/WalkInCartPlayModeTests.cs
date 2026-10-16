using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Astra's walk-in wagon (2026-10-16) in the real camp, walked with the stick: up the steps (only the figure rises,
    /// the body stays on the ground), through the doorway behind the frame, along behind the counter to the serving spot,
    /// and back out. Its seven layers sort around the figure (back, figure, counter, roof); the walls keep it off the
    /// counter and the body; the counter, bell and Store marker keep working.
    /// </summary>
    public sealed class WalkInCartPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-cart", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameRoot.Instance != null)
            {
                GameRoot.Instance.InputRouter.ScriptedMove = null;
                Object.Destroy(GameRoot.Instance.gameObject);
            }
            yield return null;
            yield return null;
            GameRoot.SaveDirectoryOverride = null;
            try { Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        private static PlayerMotor Motor => SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();

        /// <summary>Walks toward a ground point with the stick (screen up covers half the ground), up to a time limit.</summary>
        private static IEnumerator WalkTo(Vector2 target, float seconds = 8f, float near = 0.08f)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                var d = target - (Vector2)Motor.transform.position;
                if (d.magnitude < near) break;
                var stick = new Vector2(d.x, d.y * 2f);
                GameRoot.Instance.InputRouter.ScriptedMove = stick.normalized * Mathf.Clamp01(stick.magnitude * 3f + 0.3f);
                yield return null;
            }
            GameRoot.Instance.InputRouter.ScriptedMove = null;
            yield return new WaitForSeconds(0.25f);
        }

        private static float Lift => Motor.transform.Find("Visual") is Transform v ? v.localPosition.y : 0f;

        [UnityTest]
        public IEnumerator TheSupplierWalksUpIntoTheWagonServesAndWalksOut()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            for (var i = 0; i < 3; i++) yield return null;
            var root = GameRoot.Instance;
            root.Hud.Tips.HideAll();
            var cart = WalkInCart.Current;
            Assert.NotNull(cart, "the walk-in wagon replaced the single wagon sprite");
            Assert.AreSame(cart.transform, ShopView.Current.Wagon);
            Assert.IsNull(ShopView.Current.Wagon.GetComponent<SpriteRenderer>(), "no single sprite (and no assembled reference) drawn");
            var cartRenderers = cart.GetComponentsInChildren<SpriteRenderer>().Where(r => r.GetComponentInParent<OxFigure>() == null).ToList();   // the hitched ox is his own
            var layers = cartRenderers.Select(r => r.name).ToList();
            CollectionAssert.AreEquivalent(new[] { "Outline", "cart_back", "cart_shafts", "wheel_near_left", "wheel_near_right", "cart_steps", "cart_front", "cart_roof" }, layers);
            foreach (var r in cartRenderers)
            {
                Assert.AreEqual(cart.transform.position, r.transform.position, $"{r.name} on the shared pivot");
                Assert.AreEqual(Vector3.one, r.transform.lossyScale, $"{r.name} unscaled");
                Assert.AreEqual(1536f, r.sprite.rect.width, $"{r.name} keeps the shared canvas");
            }
            Assert.IsTrue(ReceiverView.TryGet("receiver.store", out var store));
            Assert.Less(Vector2.Distance(store.transform.position, cart.CustomerGround), 0.01f, "the Store's marker at the customer spot");

            // Nothing of the wagon on the carpet.
            var shop = ShopView.Current;
            foreach (var c in cart.GetComponentsInChildren<EdgeCollider2D>().SelectMany(e => e.points.Select(p => (Vector2)e.transform.TransformPoint(p))))
            {
                var (a, b) = shop.CarpetCoords(c);
                Assert.IsFalse(a > 0f && a < root.Services.Shop.CarpetSize.x && b > 0f && b < root.Services.Shop.CarpetSize.y, $"wall point {c} on the carpet");
            }

            // To the foot of the steps (from the carpet's side), then up.
            var below = cart.StepsBottom + new Vector2(-0.25f, -0.2f);
            Motor.Teleport(below);
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(0f, Motor.CartElevation, "on the ground");
            yield return Capture.Take("cart_01_steps_bottom");
            var sorting = Motor.GetComponent<SortingGroup>();
            var ground0 = Motor.transform.position;
            var halfway = Vector2.Lerp(cart.StepsBottom, cart.DoorGround, 0.45f);
            yield return WalkTo(halfway);
            Assert.That(Motor.CartElevation, Is.InRange(0.15f, 0.45f), "half way up the steps, half way up");
            Assert.AreEqual(1, sorting.sortingOrder, "in front of the door frame on the steps");
            yield return Capture.Take("cart_02_climbing");
            yield return WalkTo(cart.DoorGround);
            Assert.Less(Vector2.Distance(Motor.transform.position, cart.DoorGround), 0.2f, "through the doorway");
            Assert.AreEqual(1, sorting.sortingOrder, "in front of the frame in the doorway (Kevin, 2026-10-16: the post cut through them)");
            yield return Capture.Take("cart_03_doorway");

            // Inside, along behind the counter to the serving spot.
            yield return WalkTo(cart.ServeGround, 10f);
            Assert.Less(Vector2.Distance(Motor.transform.position, cart.ServeGround), 0.15f, "at the serving spot");
            Assert.AreEqual(cart.FloorHeight, Motor.CartElevation, 1e-4f, "on the floor, 150 px up");
            Assert.AreEqual(cart.FloorHeight, Lift, 0.08f, "only the figure is raised");
            Assert.AreEqual(Motor.transform.position.y, ((Vector2)Motor.transform.position).y, "the body stays on the ground plane");
            var front = cart.transform.Find("Front").position.y;
            var back = cart.transform.Find("Back").position.y;
            Assert.Greater(Motor.transform.position.y, front, "drawn behind the counter");
            Assert.Less(Motor.transform.position.y, back, "and in front of the back wall");
            Assert.AreEqual(0, sorting.sortingOrder);
            yield return Capture.Take("cart_04_serving");
            var target = SandboxSceneController.Current.ContextTarget;
            Assert.IsTrue(target is StationInspectable || target is BellInspectable, $"the counter (or its bell) is the button's target, not {target}");

            // Facing the customers over the counter (Kevin's playtest): holding the stick toward them turns the supplier to
            // face out, and pressing into the counter never carries them in front of it.
            var animator = Motor.GetComponentInChildren<SupplierAnimator>();
            var rowY = front;
            var until0 = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until0)
            {
                root.InputRouter.ScriptedMove = new Vector2(0.15f, -1f);
                Assert.Greater(Motor.transform.position.y, rowY, "pressed against the counter, still behind it");
                yield return null;
            }
            root.InputRouter.ScriptedMove = null;
            yield return new WaitForSeconds(0.2f);
            Assert.That(animator.Dir, Is.EqualTo("S").Or.EqualTo("SE").Or.EqualTo("SW"), "facing out over the counter");
            Assert.Greater(Motor.transform.position.y, rowY);
            yield return Capture.Take("cart_04b_facing_out");

            // The walls: down toward the counter stays inside; along to the far end stays inside.
            var before = (Vector2)Motor.transform.position;
            yield return WalkTo(before + new Vector2(0f, -1f), 1.5f);
            Assert.IsTrue(cart.Sample(Motor.transform.position, out var e1, out _) && e1 >= cart.FloorHeight - 1e-4f, "the counter holds them in");
            Assert.Greater(Motor.transform.position.y, front, "still behind the counter");
            yield return WalkTo(before + new Vector2(3f, 0.5f), 2f);
            var walls = string.Join(" | ", cart.GetComponentsInChildren<EdgeCollider2D>().Select(e => e.name + ": " + string.Join(" ", e.points.Select(q => ((Vector2)cart.transform.InverseTransformPoint(e.transform.TransformPoint(q))).ToString("F2")))));
            Assert.IsTrue(cart.Sample(Motor.transform.position, out var e2, out _) && e2 >= cart.FloorHeight - 1e-4f,
                $"the far end holds them in (at {(Vector2)cart.transform.InverseTransformPoint(Motor.transform.position):F2}; walk {string.Join(" ", cart.Walkable.Select(q => q.ToString("F2")))}; {walls})");
            yield return Capture.Take("cart_05_far_end");

            // And back out the way they came.
            yield return WalkTo(cart.ServeGround, 6f);
            yield return WalkTo(cart.DoorGround, 8f);
            yield return WalkTo(cart.StepsBottom, 6f);
            yield return WalkTo(below, 3f);
            Assert.AreEqual(0f, Motor.CartElevation, "down on the ground again");
            Assert.AreEqual(0, sorting.sortingOrder, "ordinary sorting outside");
            Assert.AreEqual(0f, Lift, 0.08f);
            yield return Capture.Take("cart_06_out");

            // From outside the counter they cannot walk into the wagon's body.
            Motor.Teleport(cart.CustomerGround + new Vector2(0f, -0.6f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.3f);
            yield return WalkTo(cart.ServeGround, 2f);
            Assert.IsFalse(cart.Sample(Motor.transform.position, out _, out _), "the counter is not a way in");
            Assert.Less(Motor.transform.position.y, front, "still in front of the counter");
            yield return Capture.Take("cart_07_customer_side");
            // Solid all round (Kevin, 2026-10-16: only in and out through the door): walked at from every side, the shafts
            // included, no one ends up standing in the wagon's body or under its shafts.
            var centre = Vector2.zero;
            foreach (var q in cart.Footprint) centre += q;
            centre = (Vector2)cart.transform.position + centre / cart.Footprint.Count;
            var shaftsMid = cart.transform.position + new Vector3(2.2f, 0.4f, 0f);
            var probes = 0;
            for (var k = 0; k < 16; k++)
            {
                var ang = k * Mathf.PI / 8f;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.5f);
                foreach (var aim in new[] { centre, (Vector2)shaftsMid })
                {
                    var from = aim + dir * 2.6f;
                    var fromLocal = (Vector2)cart.transform.InverseTransformPoint(from);
                    if (!MineSlabView.Current.SurfaceWalkable(from) || Vector2.Distance(from, cart.StepsBottom) < 0.9f
                        || WalkInCart.Contains(cart.Footprint, fromLocal) || cart.Sample(from, out _, out _)
                        || WalkInCart.DistanceToPolygon(cart.Footprint, fromLocal) < root.Config.Balance.playerFootprintRadius * 2.2f) continue;   // start outside its walls
                    Motor.Teleport(from);
                    SandboxSceneController.Current.SnapPlayerToSavedPosition();
                    yield return null;
                    if (Vector2.Distance(Motor.transform.position, from) > 0.05f) continue;   // not somewhere the supplier may stand
                    probes++;
                    // Walk at it, noting whether the way went up the steps and through the door (the one way in).
                    var viaDoor = false;
                    var until = Time.realtimeSinceStartup + 1.6f;
                    var trail = new System.Text.StringBuilder();
                    var last = (Vector2)Motor.transform.position;
                    while (Time.realtimeSinceStartup < until)
                    {
                        var now = (Vector2)Motor.transform.position;
                        var lp = (Vector2)cart.transform.InverseTransformPoint(now);
                        if (trail.Length < 1500) trail.Append(lp.ToString("F2")).Append(Vector2.Distance(now, last) > 0.2f ? "JUMP " : " ");
                        last = now;
                        var d = aim - (Vector2)Motor.transform.position;
                        var stick = new Vector2(d.x, d.y * 2f);
                        GameRoot.Instance.InputRouter.ScriptedMove = stick.normalized;
                        if (cart.Sample(Motor.transform.position, out _, out var atDoor) && atDoor) viaDoor = true;
                        yield return null;
                    }
                    GameRoot.Instance.InputRouter.ScriptedMove = null;
                    var local = (Vector2)cart.transform.InverseTransformPoint(Motor.transform.position);
                    Assert.IsFalse(!viaDoor && WalkInCart.Contains(cart.Footprint, local), $"walked into the wagon's body from {from - aim:F2} (at {local:F2}) without the door: {trail}");
                    Assert.Greater(Vector2.Distance(Motor.transform.position, shaftsMid), 0.25f, $"walked through the shafts from {from - aim:F2}");
                }
            }
            Assert.Greater(probes, 8, "probed from most sides");
            yield return Capture.Take("cart_08_solid");
            CollectionAssert.IsEmpty(root.Services.Logistics.System.Audit());
            Assert.AreEqual(ground0.z, Motor.transform.position.z);
        }
    }
}
