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
    /// Astra's equipment handoff (2026-10-16) in the real shop: the neckless mannequin empty; each body armour with the
    /// head seated in its collar (the torso hidden); each headwear and shield on the bare mannequin; armour, helmet and
    /// shield together. Every layer on the stand's own canvas and pivot, unscaled. The blond citizen wears headwear in
    /// the SE view it was fitted to (and its SW mirror), not in the others, and keeps walking.
    /// </summary>
    public sealed class ArmourStandPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-armour", Guid.NewGuid().ToString("N"));
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

        private static IEnumerator Boot()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            for (var i = 0; i < 3; i++) yield return null;
            GameRoot.Instance.Hud.Tips.HideAll();
        }

        private static void AssertNone((Sprite armor, Sprite head, Sprite headwear, Sprite shield) shown)
        {
            Assert.IsNull(shown.armor);
            Assert.IsNull(shown.head);
            Assert.IsNull(shown.headwear);
            Assert.IsNull(shown.shield);
        }

        private static ItemDefinition Item(string key) => GameRoot.Instance.Services.Catalogue.Get<ItemDefinition>("item." + key);

        [UnityTest]
        public IEnumerator TheStandWearsArmourHeadwearAndShieldsAsLayers()
        {
            yield return Boot();
            var stand = ShopView.Current.Views.Values.First(v => v.Definition.Display == FurnitureDisplay.Armor && v.Facing == Facing.A);
            Assert.IsTrue(stand.LayeredStand, "the stand in facing A shows equipment as layers");
            var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            motor.Teleport((Vector2)stand.transform.position + new Vector2(1.2f, -1.4f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.6f);
            var level = stand.Definition.At(stand.Level).facingA;
            // Nothing tall stands in front of it in the starting layout (2026-10-16: the weapon stand hid the shield).
            var standPlace = GameRoot.Instance.Services.Shop.State.Furniture.First(f => f.Id == stand.PlacementId);
            Assert.AreEqual((3, 0), (standPlace.A, standPlace.B), "the front-right corner of the carpet");
            foreach (var other in ShopView.Current.Views.Values)
                if (other != stand && other.Definition.Display != FurnitureDisplay.Small
                    && Mathf.Abs(other.transform.position.x - stand.transform.position.x) < 1.2f)   // close enough to cover it
                    Assert.Greater(other.transform.position.y, stand.transform.position.y - 0.01f, $"{other.name} is not in front of the stand");

            // Empty: the approved neckless mannequin.
            stand.SetStock(Array.Empty<(ItemDefinition, int)>());
            yield return null;
            Assert.AreSame(level.sprite, stand.Visual.sprite);
            AssertNone(stand.EquipmentShown);
            yield return Capture.Take("armour_stand_empty");

            void Aligned()
            {
                foreach (var sr in stand.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!sr.enabled || sr.sprite == null || sr.name == "Outline" || sr.name == "Slot") continue;
                    Assert.AreEqual(stand.transform.position, sr.transform.position, $"{sr.name} sits on the stand's ground point");
                    Assert.AreEqual(Vector3.one, sr.transform.lossyScale, $"{sr.name} is unscaled");
                    Assert.AreEqual(512f, sr.sprite.rect.width, $"{sr.name} keeps the shared canvas");
                    Assert.AreEqual(stand.Visual.sprite.pivot, sr.sprite.pivot, $"{sr.name} shares the stand's pivot");
                }
            }

            // Each body armour, through the shop's own stock path: the support alone, the armour, the head in its collar.
            foreach (var key in new[] { "riveted_coat", "fur_brigandine", "steel_coat", "moonsilver_mail" })
            {
                stand.SetStock(new[] { (Item(key), 1) });
                yield return null;
                Assert.AreSame(level.armored, stand.Visual.sprite, "the torso is hidden under the armour");
                var shown = stand.EquipmentShown;
                Assert.AreSame(Item(key).DisplaySprite(false, out _), shown.armor);
                Assert.AreSame(Item(key).StandHeadA, shown.head, "the head seated in this armour's collar");
                Aligned();
                yield return Capture.Take($"armour_stand_{key}");
            }
            // Headwear alone, then shields alone, on the bare mannequin.
            foreach (var key in new[] { "leather_cap", "iron_helm", "steel_helm", "moonsilver_circlet", "wooden_shield", "iron_shield", "steel_kite_shield" })
            {
                stand.SetStock(new[] { (Item(key), 2) });
                yield return null;
                Assert.AreSame(level.sprite, stand.Visual.sprite, "the bare mannequin");
                var shown = stand.EquipmentShown;
                Assert.IsNull(shown.armor);
                Assert.AreSame(Item(key).DisplaySprite(false, out _), Item(key).Category == ItemCategory.Helmet ? shown.headwear : shown.shield);
                Assert.AreEqual(1, stand.VisibleCount);
                Aligned();
                yield return Capture.Take($"armour_stand_{key}");
            }
            // Together: armour, helmet and shield (the shield beside the chest, which stays visible).
            foreach (var (armor, helm, shield) in new[] { ("steel_coat", "steel_helm", "steel_kite_shield"), ("riveted_coat", "leather_cap", "wooden_shield"),
                         ("fur_brigandine", "iron_helm", "iron_shield"), ("moonsilver_mail", "moonsilver_circlet", "steel_kite_shield") })
            {
                stand.SetStock(new[] { (Item(shield), 3), (Item(armor), 1), (Item(helm), 2) });   // any order: each to its place
                yield return null;
                Assert.AreEqual(3, stand.VisibleCount, "a stand shows a set (Kevin, 2026-10-16)");
                var badges = stand.GetComponentsInChildren<TMPro.TextMeshPro>().Select(t => t.text).ToList();
                CollectionAssert.AreEquivalent(new[] { "", "×2", "×3" }, badges, "each piece its own count");
                var shown = stand.EquipmentShown;
                Assert.IsNotNull(shown.armor);
                Assert.IsNotNull(shown.head);
                Assert.IsNotNull(shown.headwear);
                Assert.IsNotNull(shown.shield);
                var orders = stand.GetComponentsInChildren<SpriteRenderer>().Where(r => r.enabled && r.name != "Outline" && r.sprite != null)
                    .ToDictionary(r => r.name, r => r.sortingOrder);
                Assert.That(orders["Visual"] < orders["Armour"] && orders["Armour"] < orders["Head"] && orders["Head"] < orders["Headwear"] && orders["Headwear"] < orders["Shield"],
                    "back to front: support, armour, head, headwear, shield");
                Aligned();
                yield return Capture.Take($"armour_stand_set_{armor}");
            }

            // Facing B: no view was supplied, so the stand keeps its fitted views there (body armour's old B view; others stack).
            var shop = GameRoot.Instance.Services.Shop;
            var placement = shop.State.Furniture.First(f => f.Id == stand.PlacementId);
            stand.Show(new Placement { Id = placement.Id, FurnitureId = placement.FurnitureId, Level = placement.Level, A = placement.A, B = placement.B, Facing = Facing.B },
                stand.transform.position, (1, 1));
            stand.SetStock(new[] { (Item("steel_coat"), 1) });
            yield return null;
            Assert.IsFalse(stand.LayeredStand);
            AssertNone(stand.EquipmentShown);
            yield return Capture.Take("armour_stand_facing_B");
        }

        [UnityTest]
        public IEnumerator TheBlondCitizenWearsHeadwearInTheViewItWasFittedTo()
        {
            yield return Boot();
            var crowd = Object.FindAnyObjectByType<ApplicantCrowdView>();
            Assert.NotNull(crowd.Citizen);
            var player = SandboxSceneController.Current.Player;
            var at = (Vector2)player.position + new Vector2(0f, 1.2f);
            foreach (var key in new[] { "leather_cap", "iron_helm", "steel_helm", "moonsilver_circlet" })
            {
                var fig = CitizenFigure.Create(null, "Citizen " + key, at, crowd.Citizen, crowd.Animations);
                var rig = fig.GetComponentInChildren<HoseRig>();
                rig.HeadwearItem = "item." + key;
                // Walk south-east (screen right and down): the SE view, the one fitted.
                for (var i = 0; i < 40; i++)
                {
                    fig.transform.position += new Vector3(0.02f, -0.01f, 0f);
                    yield return null;
                }
                Assert.AreEqual("SE", rig.ViewName);
                Assert.IsFalse(rig.Mirrored);
                Assert.IsTrue(rig.HeadwearShown, $"{key} worn in SE");
                yield return Capture.Take($"citizen_headwear_{key}_SE");
                // South-west: the SE view mirrored, the headwear with it.
                for (var i = 0; i < 40; i++)
                {
                    fig.transform.position += new Vector3(-0.02f, -0.01f, 0f);
                    yield return null;
                }
                Assert.AreEqual("SE", rig.ViewName);
                Assert.IsTrue(rig.Mirrored);
                Assert.IsTrue(rig.HeadwearShown, $"{key} worn in SW (the SE view mirrored)");
                // East: fitted too since Astra's armour v2 (2026-10-18): every view has its helmet now.
                for (var i = 0; i < 40; i++)
                {
                    fig.transform.position += new Vector3(0.03f, 0f, 0f);
                    yield return null;
                }
                Assert.AreEqual("E", rig.ViewName);
                Assert.IsTrue(rig.HeadwearShown, $"{key} worn in E (every view supplied in v2)");
                Object.Destroy(fig);
                yield return null;
            }
        }
    }
}
