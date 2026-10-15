using System;
using System.Collections;
using System.Linq;
using System.IO;
using NUnit.Framework;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>Phase 3 acceptance in the real scene: craft → output pad → carry → Store racks, and processing.</summary>
    public sealed class Phase3PlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-phase3", Guid.NewGuid().ToString("N"));
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
            yield return null;
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.NotNull(SandboxSceneController.Current);
            for (var i = 0; i < 3; i++) yield return null;
        }

        private static IEnumerator StandAt(Vector2 p, float seconds)
        {
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(p);
            var end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        private static StationInspectable Station(string name) => GameObject.Find(name).GetComponent<StationInspectable>();

        private static FurnitureView Piece(string id) => ShopView.Current.Views.Values.First(v => v.Definition.Id == id);

        [UnityTest]
        public IEnumerator CraftCarryStockAndInspect()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var log = s.Logistics.System;
            SandboxSceneController.Current.Inspect(Station("Forge"));
            yield return null;
            var panel = GameRoot.Instance.Hud.StationPanel;
            Assert.IsTrue(panel.IsOpen);
            Assert.IsTrue(panel.Craft("recipe.iron_sword"), panel.LastMessage);
            Assert.IsTrue(panel.Craft("recipe.iron_dagger"), panel.LastMessage);
            s.Production.DebugFinishCrafts();   // crafting is timed (the crafting expansion): done now for the test
            Assert.AreEqual(1, log.SharedAvailable("resource.iron_ingot"), "6 - 3 - 2");
            Assert.IsFalse(panel.Craft("recipe.riveted_coat"), "not enough iron left");
            StringAssert.Contains("Iron Ingot", panel.LastMessage, "blocked reasons use display names");
            Assert.AreEqual(1, log.SharedAvailable("resource.iron_ingot"), "a blocked craft spends nothing");
            yield return new WaitForSeconds(0.3f);
            yield return Capture.Take("phase3_forge_panel");
            panel.Close();

            yield return StandAt(new Vector2(-4.3f, -4.0f), 0.6f);   // forge output pad
            Assert.AreEqual(2, log.CarryCount);
            yield return StandAt(Phase1PlayModeTests.Spot("receiver.store"), 0.6f);  // store receiving strip
            Assert.AreEqual(2, log.State.StoreItems.Count);
            yield return null;
            Assert.AreEqual(1, Piece("furniture.weapon_stand").VisibleCount, "the sword on the weapon stand");
            Assert.AreEqual(1, Piece("furniture.display_table").VisibleCount, "the dagger on the display table");
            yield return Capture.Take("phase3_store_racks");

            GameRoot.Instance.Hud.ShowStore();
            GameRoot.Instance.Hud.StorePanel.Select(StorePanel.Tab.Weapons);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(2, GameRoot.Instance.Hud.StorePanel.RowCount);
            yield return Capture.Take("phase3_store_panel");
            GameRoot.Instance.Hud.StorePanel.Select(StorePanel.Tab.Blueprints);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(13, GameRoot.Instance.Hud.StorePanel.RowCount, "the six starter blueprints, the helmets and the shields");
            GameRoot.Instance.Hud.StorePanel.Close();
            CollectionAssert.IsEmpty(log.Audit());
        }

        [UnityTest]
        public IEnumerator DeliveredOreIsSmeltedAndOverflowStockStaysAccessible()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var log = s.Logistics.System;
            s.Sites.SpeedMultiplier = 1f;
            log.CreateMaterialsInContainer("container.mine_staging", "resource.iron_ore", 10);   // test fixture
            yield return StandAt(Phase1PlayModeTests.Spot("container.mine_staging"), 1.2f);
            yield return StandAt(Phase1PlayModeTests.Spot("receiver.smelter"), 1.2f);    // deliver to the smelter
            Assert.Greater(log.SharedAvailable("resource.iron_ore") + 2, 0);
            yield return new WaitForSeconds(0.5f);
            var bar = GameObject.Find("Smelter").transform.Find("ProcessorBar").gameObject;
            Assert.IsTrue(bar.activeSelf, "processing bar shows while a batch runs");
            SandboxSceneController.Current.Inspect(Station("Smelter"));
            yield return new WaitForSeconds(0.3f);
            yield return Capture.Take("phase3_smelter_panel");
            GameRoot.Instance.Hud.StationPanel.Close();
            yield return new WaitForSeconds(4.2f);
            Assert.Greater(log.SharedAvailable("resource.iron_ingot"), 6, "an ingot arrived without any extra haul");

            // Overflow: many copies of one item; racks show a subset, the inventory shows every unit.
            s.Production.DebugGrantMaterials(new System.Collections.Generic.List<Content.ResourceCost>
            {
                new Content.ResourceCost { resource = s.Catalogue.Get<Content.ResourceDefinition>("resource.herb"), quantity = 20 },
            });
            for (var round = 0; round < 2; round++)
            {
                for (var i = 0; i < 5; i++)
                {
                    Assert.IsTrue(s.Production.Craft("recipe.recovery_tonic", out var f), f);
                    s.Production.DebugFinishCrafts();
                }
                yield return StandAt(new Vector2(-3.85f, -1.95f), 0.8f);   // apothecary pad
                yield return StandAt(Phase1PlayModeTests.Spot("receiver.store"), 0.8f);
            }
            Assert.AreEqual(10, log.State.StoreItems.Count);
            var shelf = Piece("furniture.potion_stand");
            yield return null;
            Assert.AreEqual(1, shelf.VisibleCount, "one distinct potion shown with a ×10 badge");
            CollectionAssert.IsEmpty(log.Audit());
        }
    }
}
