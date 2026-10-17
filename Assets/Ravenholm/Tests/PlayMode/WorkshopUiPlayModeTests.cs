using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    public sealed class WorkshopUiPlayModeTests
    {
        private string _dir;
        [UnitySetUp] public IEnumerator Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-workshop-ui", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            yield return null;
            var until = Time.realtimeSinceStartup + 30;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox") && Time.realtimeSinceStartup < until) yield return null;
            Assert.NotNull(SandboxSceneController.Current);
            GameRoot.Instance.Settings.Update(x => { x.DeveloperMode = false; x.TextScale = 1; });
            GameRoot.Instance.Hud.Tips.HideAll();
            yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (GameRoot.Instance != null) Object.Destroy(GameRoot.Instance.gameObject);
            yield return null; yield return null;
            Assert.IsNull(GameObject.Find("EquipmentPortraitStage"), "portrait resources are released");
            GameRoot.SaveDirectoryOverride = null;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
        [UnityTest] public IEnumerator GearPickerReservesChangesRejectsIncompatibleItemsAndReleasesOnClose()
        {
            var root = GameRoot.Instance; var services = root.Services;
            var log = services.Logistics.System;
            foreach (var id in new[] { "item.iron_sword", "item.iron_dagger", "item.cedar_bow", "item.cedar_staff", "item.riveted_coat", "item.recovery_tonic" }) log.CreateStoreItem(id, "UI test");
            services.Jobs.AdvanceMs(240000);
            var applicant = services.Jobs.State.Posting.Applicants.First(a => services.Jobs.Board.HasArrived(a));
            root.Hud.ShowLoadout(applicant.ContractorId);
            yield return new WaitForSeconds(.25f);
            var panel = root.Hud.Loadout;
            Assert.AreEqual(6, panel.transform.Find("Card/Equipment").GetComponentsInChildren<Button>().Length);
            panel.transform.Find("Card/Equipment/SlotArmor").GetComponent<Button>().onClick.Invoke();
            yield return null;
            var armor = log.StoreItems.First(i => log.DefinitionOfItem(i) == "item.riveted_coat");
            var weapon = log.StoreItems.First(i => log.DefinitionOfItem(i) == "item.iron_dagger");
            Assert.IsFalse(panel.EquipSelected(weapon), "weapons cannot be put in body slot");
            Assert.IsTrue(panel.EquipSelected(null)); Assert.IsNull(panel.Current.Armor);
            var armorButton = panel.transform.Find("Card/Choices/ItemViewport/Items").GetComponentsInChildren<Button>().First();
            armorButton.onClick.Invoke();
            Assert.AreEqual(armor, panel.Current.Armor);
            Assert.IsTrue(log.IsReservedByOther(armor, "another owner"));
            panel.SelectSlot(0);
            yield return null;
            var portrait = panel.GetComponentInChildren<WorkshopHeroPreview>();
            Assert.IsTrue(portrait.ArmorShown);
            Assert.AreEqual("item.riveted_coat", portrait.ShownArmorId);
            Assert.IsTrue(portrait.WeaponShown);
            yield return Capture.Take("workshop_equipment");
            root.Settings.Update(x => x.TextScale = 1.3f);
            yield return Capture.Take("workshop_equipment_small", 750, 1334);
            var card = (RectTransform)panel.transform.Find("Card");
            foreach (var name in new[] { "Back", "Send", "Close" }) AssertInside(card, (RectTransform)card.Find(name));
            panel.Close(); yield return null;
            Assert.IsFalse(log.IsReservedByOther(armor, "another owner"));
            root.Hud.ShowLoadout(applicant.ContractorId); yield return null;
            panel.Close(); yield return null;
            Assert.IsNull(GameObject.Find("EquipmentPortraitStage"));
            CollectionAssert.IsEmpty(log.Audit());
        }
        [UnityTest] public IEnumerator CraftButtonsTabsAndPinnedCloseWorkAtBothPortraitSizes()
        {
            var root = GameRoot.Instance;
            root.Hud.ShowStation(GameObject.Find("Forge").GetComponent<StationInspectable>());
            yield return new WaitForSeconds(.25f);
            var panel = root.Hud.StationPanel;
            yield return Capture.Take("workshop_forge");
            var craft = panel.transform.Find("Card/Viewport/Content/Recipe_recipe.iron_sword/Craft").GetComponent<Button>();
            Assert.IsTrue(craft.interactable);
            craft.onClick.Invoke();
            Assert.Greater(root.Services.Production.System.BusySlots("station.forge"), 0);
            panel.transform.Find("Card/UpgradeTab").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.IsNull(panel.transform.Find("Card/Viewport/Content/Recipe_recipe.iron_sword"));
            yield return Capture.Take("workshop_forge_upgrade");
            panel.transform.Find("Card/CraftTab").GetComponent<Button>().onClick.Invoke();
            root.Settings.Update(x => x.TextScale = 1.3f);
            yield return Capture.Take("workshop_forge_small", 750, 1334);
            var card = (RectTransform)panel.transform.Find("Card");
            AssertInside(card, (RectTransform)card.Find("Close"));
            panel.transform.Find("Card/Close").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(panel.IsOpen);
        }
        [UnityTest] public IEnumerator PortraitShowsEveryArmorAndRemovesEquipmentWithoutStaleLayers()
        {
            var root = GameRoot.Instance; var services = root.Services;
            services.Jobs.AdvanceMs(240000);
            var applicant = services.Jobs.State.Posting.Applicants.First(a => services.Jobs.Board.HasArrived(a));
            foreach (var id in new[] { "item.riveted_coat", "item.iron_dagger", "item.iron_sword", "item.cedar_bow", "item.cedar_staff", "item.iron_helm", "item.wooden_shield" })
                services.Logistics.System.CreateStoreItem(id, "Portrait test");
            root.Hud.ShowLoadout(applicant.ContractorId);
            var panel = root.Hud.Loadout;
            var portrait = panel.GetComponentInChildren<WorkshopHeroPreview>();
            panel.SelectSlot(5);
            Assert.IsTrue(panel.EquipSelected(services.Logistics.System.StoreItems.First(i => services.Logistics.System.DefinitionOfItem(i) == "item.iron_helm")));
            panel.SelectSlot(4);
            var needsBothHands = services.Catalogue.Get<Ravenholm.Content.ItemDefinition>(services.Logistics.System.DefinitionOfItem(panel.Current.Weapon)).TwoHanded;
            var shieldChosen = panel.EquipSelected(services.Logistics.System.StoreItems.First(i => services.Logistics.System.DefinitionOfItem(i) == "item.wooden_shield"));
            Assert.AreEqual(!needsBothHands, shieldChosen);
            yield return new WaitForSeconds(.3f);
            Assert.IsTrue(portrait.HeadwearShown); Assert.AreEqual(!needsBothHands, portrait.ShieldShown);
            Assert.IsTrue(portrait.ArmorShown); Assert.IsTrue(portrait.WeaponShown);
            yield return Capture.Take("equipped_hero_live");
            panel.SelectSlot(1); Assert.IsTrue(panel.EquipSelected(null));
            panel.SelectSlot(0); Assert.IsTrue(panel.EquipSelected(null));
            panel.SelectSlot(4); Assert.IsTrue(panel.EquipSelected(null));
            panel.SelectSlot(5); Assert.IsTrue(panel.EquipSelected(null));
            yield return null;
            Assert.IsFalse(portrait.ArmorShown); Assert.IsFalse(portrait.WeaponShown);
            Assert.IsFalse(portrait.ShieldShown); Assert.IsFalse(portrait.HeadwearShown);
            yield return Capture.Take("equipped_hero_empty");
            // Check the visual component independently of class compatibility and item reservations.
            var cat = services.Catalogue;
            var sword = cat.Get<Ravenholm.Content.ItemDefinition>("item.iron_sword");
            var shield = cat.Get<Ravenholm.Content.ItemDefinition>("item.wooden_shield");
            var helm = cat.Get<Ravenholm.Content.ItemDefinition>("item.iron_helm");
            foreach (var id in new[] { "item.riveted_coat", "item.fur_brigandine", "item.steel_coat", "item.moonsilver_mail", "item.leather_jerkin", "item.padded_gambeson" })
            {
                // Disable panel refresh while inspecting a synthetic visual combination; the production code uses the loadout.
                panel.enabled = false;
                portrait.SetEquipment(helm, cat.Get<Ravenholm.Content.ItemDefinition>(id), sword, shield);
                yield return null;
                Assert.AreEqual(id, portrait.ShownArmorId);
                Assert.AreEqual(sword.Id, portrait.ShownWeaponId);
                Assert.AreEqual(shield.Id, portrait.ShownShieldId);
                yield return Capture.Take("portrait_" + id.Replace("item.", ""));
            }
            portrait.SetEquipment(null, null, cat.Get<Ravenholm.Content.ItemDefinition>("item.steel_maul"), shield);
            yield return null;
            Assert.IsFalse(portrait.ShieldShown, "two-handed weapons hide incompatible shields");
            Assert.IsFalse(portrait.ArmorShown); Assert.IsFalse(portrait.HeadwearShown);
            Assert.AreEqual("item.steel_maul", portrait.ShownWeaponId);
            yield return Capture.Take("portrait_two_handed");
            panel.enabled = true;
            panel.Close(); yield return null;
            Assert.IsNull(GameObject.Find("EquipmentPortraitStage"));
        }

        private static void AssertInside(RectTransform parent, RectTransform child)
        {
            var corners = new Vector3[4]; child.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var local = (Vector2)parent.InverseTransformPoint(corner);
                Assert.GreaterOrEqual(local.x, parent.rect.xMin - 1); Assert.LessOrEqual(local.x, parent.rect.xMax + 1);
                Assert.GreaterOrEqual(local.y, parent.rect.yMin - 1); Assert.LessOrEqual(local.y, parent.rect.yMax + 1);
            }
        }
    }
}
