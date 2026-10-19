using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Shop;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Astra's approved shop displays (2026-10-19): the weapon cradle and the potion display at the wagon, in her approved
    /// angle (the game's facing B), stocked: swords turned and seated by their tips under the tip rail, bottles on their
    /// pads back row first, the maul kept upright in its box, footprints that block walking but leave the wagon's steps free.
    /// </summary>
    public sealed class ShopDisplaysPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-displays", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameRoot.Instance != null) Object.Destroy(GameRoot.Instance.gameObject);
            yield return null;
            yield return null;
            GameRoot.SaveDirectoryOverride = null;
            try { Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        private static FurnitureView Piece(string id) => ShopView.Current.Views.Values.First(v => v.Definition.Id == id);

        [UnityTest]
        public IEnumerator TheApprovedDisplaysSeatTheirItems()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            yield return null;
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.NotNull(ShopView.Current);
            var s = GameRoot.Instance.Services;
            GameRoot.Instance.Hud.Tips.HideAll();
            foreach (var id in new[] { "item.iron_sword", "item.steel_sword", "item.moonsilver_blade",
                         "item.recovery_tonic", "item.strong_tonic", "item.antidote", "item.pathfinder_draught", "item.trapsense_draught", "item.warding_draught" })
                s.Logistics.System.CreateStoreItem(id, "test stock");
            var player = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            player.Teleport(ShopView.Current.TileCentre(1, -1));
            yield return new WaitForSeconds(0.5f);

            var rack = Piece("furniture.weapon_stand");
            var potions = Piece("furniture.potion_stand");
            Assert.AreEqual(Facing.B, rack.Facing, "the starting cradle stands in Astra's approved angle");
            Assert.AreEqual(Facing.B, potions.Facing);
            Assert.AreEqual("weapon_cradle_1_A", rack.Visual.sprite.name);
            Assert.AreEqual("potion_display_1_A", potions.Visual.sprite.name);
            Assert.AreEqual(3, rack.SlotCount);
            Assert.AreEqual(6, potions.SlotCount);

            // The cradle: three swords on their front views, turned blade down, under the tip rail, over the base.
            Assert.AreEqual(3, rack.VisibleCount);
            Assert.NotNull(rack.Foreground, "the tip rail");
            Assert.AreEqual(30, rack.Foreground.sortingOrder);
            for (var i = 0; i < 3; i++)
            {
                var sr = rack.ItemRenderers[i];
                StringAssert.EndsWith("_front", sr.sprite.name);
                Assert.AreEqual(10 + i, sr.sortingOrder);
                Assert.Less(Mathf.Abs(Mathf.DeltaAngle(sr.transform.localEulerAngles.z, -184.5f)), 0.6f, "turned 184.5 degrees clockwise");
                // The blade's tip (the sprite's lowest point) on the slot's anchor.
                var pose = rack.Definition.At(1).facingB.poses[i];
                var anchor = rack.transform.TransformPoint(new Vector2((pose.anchor.x - 256f) / 256f, (420f - pose.anchor.y) / 256f));
                var item = rack.CurrentStock[i].def;
                var sp = sr.sprite;
                var tipPx = item.Views.frontTip;
                var tip = sr.transform.TransformPoint(new Vector2((tipPx.x - sp.pivot.x) / sp.pixelsPerUnit, (sp.rect.height - sp.pivot.y - tipPx.y) / sp.pixelsPerUnit));
                Assert.Less(Vector2.Distance(anchor, tip), 0.02f, $"sword {i} rests on its tip");
                var visibleHeight = item.Views.frontBounds.height / sp.pixelsPerUnit * sr.transform.localScale.y;
                Assert.Greater(visibleHeight, 0.5f, "fitted to the slot's height, not shrunk");
                Assert.Less(visibleHeight, 146f / 256f + 0.01f, "and never past it");
            }

            // The potion display: six bottles, the back row drawn first, each standing on its pad.
            Assert.AreEqual(6, potions.VisibleCount);
            Assert.IsNull(potions.Foreground);
            for (var i = 0; i < 6; i++)
            {
                var sr = potions.ItemRenderers[i];
                StringAssert.EndsWith("_front", sr.sprite.name);
                Assert.AreEqual(10 + i, sr.sortingOrder, "slots 1-3 (back) before 4-6 (front)");
                var pose = potions.Definition.At(1).facingB.poses[i];
                var anchor = potions.transform.TransformPoint(new Vector2((pose.anchor.x - 256f) / 256f, (420f - pose.anchor.y) / 256f));
                var vis = potions.CurrentStock[i].def.Views.frontBounds;
                var sp = sr.sprite;
                var foot = sr.transform.TransformPoint(new Vector2((vis.center.x - sp.pivot.x) / sp.pixelsPerUnit, (sp.rect.height - sp.pivot.y - vis.yMax) / sp.pixelsPerUnit));
                Assert.Less(Vector2.Distance(anchor, foot), 0.02f, $"bottle {i} stands on its pad");
                Assert.AreEqual(Quaternion.identity, sr.transform.localRotation);
            }

            // A maul is not a blade: the cradle does not take it (it would hang upside down by its head).
            s.Logistics.System.CreateStoreItem("item.steel_maul", "test stock");
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(rack.CurrentStock.Any(x => x.def != null && x.def.Id == "item.steel_maul"), "the maul is not seated on the cradle");
            Assert.AreEqual(3, rack.VisibleCount, "the three blades stay");

            // Collisions: both block walking on their footprints; the wagon's steps stay clear.
            var steps = WalkInCart.Current.StepsBottom;
            foreach (var piece in new[] { rack, potions })
            {
                var foot = piece.GetComponentInChildren<PolygonCollider2D>();
                Assert.NotNull(foot, "a footprint that blocks walking");
                Assert.IsTrue(foot.OverlapPoint(piece.transform.position), "standing on its ground point");
                Assert.Greater(Vector2.Distance(foot.ClosestPoint(steps), steps), 0.35f, "room to reach the wagon's steps");
            }

            // Sorting against a character: in front of the cradle he draws over it, behind it he is hidden by it.
            var hero = SandboxSceneController.Current.Player;
            player.Teleport((Vector2)potions.transform.position + new Vector2(0f, 0.9f));
            yield return new WaitForSeconds(0.3f);
            yield return Capture.Take("shop_displays_behind");
            player.Teleport((Vector2)rack.transform.position + new Vector2(0.2f, -0.75f));
            yield return new WaitForSeconds(0.3f);
            Assert.Less(hero.transform.position.y, rack.transform.position.y, "he stands in front of the cradle");
            yield return Capture.Take("shop_displays");
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }
    }
}
