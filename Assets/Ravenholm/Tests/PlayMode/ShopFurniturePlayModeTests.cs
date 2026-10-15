using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Astra's shop furniture (2026-10-15): the desk, display table, armour stand and potion shelves stand in the Store,
    /// show the stock in their slot boxes, and grow with the Shop's level, bought at the service desk.
    /// </summary>
    public sealed class ShopFurniturePlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-shop", Guid.NewGuid().ToString("N"));
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

        private static FurnitureView Piece(string name) => GameObject.Find(name).GetComponent<FurnitureView>();

        /// <summary>Every shown item lies inside its slot box (a little slack for the outline).</summary>
        private static void AssertItemsInBoxes(FurnitureView f)
        {
            var items = f.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.name == "Slot").ToList();
            Assert.AreEqual(f.SlotCount, items.Count, $"{f.name}: one renderer per slot");
            for (var i = 0; i < items.Count; i++)
            {
                if (!items[i].gameObject.activeInHierarchy) continue;
                var box = f.Boxes[i];
                var min = f.transform.TransformPoint(box.min);
                var max = f.transform.TransformPoint(box.max);
                var b = items[i].bounds;
                const float slack = 0.01f;
                Assert.IsTrue(b.min.x >= min.x - slack && b.max.x <= max.x + slack && b.min.y >= min.y - slack && b.max.y <= max.y + slack,
                    $"{f.name} slot {i}: {items[i].sprite.name} {b.min}..{b.max} fits {min}..{max}");
                var fillsWidth = Mathf.Abs(b.size.x - (max.x - min.x)) < 0.01f;
                var fillsHeight = Mathf.Abs(b.size.y - (max.y - min.y)) < 0.01f;
                Assert.IsTrue(fillsWidth || fillsHeight, $"{f.name} slot {i}: scaled uniformly to fill its box");
            }
        }

        [UnityTest]
        public IEnumerator FurnitureShowsTheStockAndGrowsWithTheShop()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var hud = GameRoot.Instance.Hud;
            foreach (var id in new[]
                     {
                         "item.iron_sword", "item.steel_maul", "item.steel_coat", "item.riveted_coat", "item.iron_dagger", "item.iron_dagger",
                         "item.steel_stiletto", "item.moonsilver_focus", "item.antidote", "item.strong_tonic", "item.recovery_tonic",
                         "item.pathfinder_draught", "item.warding_draught", "item.trapsense_draught",
                     })
                s.Logistics.System.CreateStoreItem(id, "test stock");
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(new Vector2(0.9f, 2.35f));   // outside the desk
            yield return new WaitForSeconds(0.5f);

            var desk = Piece("Service Desk");
            var table = Piece("Display Table");
            var armor = Piece("Armor Stand");
            var shelf = Piece("Potion Shelf");
            var expect = new[] { (0, 2, 1, 4), (0, 3, 1, 6), (0, 4, 1, 9) };   // slots: desk, table, armour stand, potion shelf
            for (var level = 1; level <= 3; level++)
            {
                if (level > 1)
                {
                    // Bought at the service desk: tap it for the Shop's upgrade.
                    s.Jobs.DebugSetGold(500);
                    foreach (var (id, q) in new[] { ("cedar_lumber", 20), ("iron_ingot", 10), ("maple_lumber", 10), ("steel_ingot", 10), ("copper_ingot", 4) })
                        s.Logistics.System.ProduceShared("resource." + id, q, "test grant");
                    SandboxSceneController.Current.Inspect(desk.GetComponent<StationInspectable>());
                    yield return new WaitForSeconds(0.3f);
                    Assert.IsTrue(hud.StationPanel.IsOpen, "the desk opens the Shop's panel");
                    var up = s.Catalogue.Get<UpgradeDefinition>($"upgrade.store_{level}");
                    if (level == 2) yield return Capture.Take("shop_desk_upgrade");
                    Assert.IsTrue(hud.StationPanel.BuyUpgrade(up), hud.StationPanel.LastMessage);
                    hud.StationPanel.Close();
                    yield return new WaitForSeconds(0.4f);
                }
                Assert.AreEqual(level, s.Production.System.Level("station.store"));
                var (d, t, a, p) = expect[level - 1];
                foreach (var (f, n) in new[] { (desk, d), (table, t), (armor, a), (shelf, p) })
                {
                    Assert.AreEqual(level, f.Level, $"{f.name} shows level {level}");
                    Assert.AreEqual(n, f.SlotCount, $"{f.name} level {level}: slots");
                    StringAssert.EndsWith("_" + level, f.GetComponentInChildren<SpriteRenderer>().sprite.name);
                    AssertItemsInBoxes(f);
                }
                Assert.AreEqual(Mathf.Min(3, t), table.VisibleCount, "dagger, stiletto and focus on the table (the duplicate dagger counted once)");
                Assert.AreEqual(1, armor.VisibleCount);
                Assert.AreEqual(Mathf.Min(6, p), shelf.VisibleCount);
                var rack = Object.FindObjectsByType<RackView>(FindObjectsSortMode.None).Single();
                Assert.AreEqual(2, rack.VisibleCount, "the weapon rack keeps the large weapons; small pieces are on the table");
                yield return Capture.Take($"shop_furniture_{level}");
            }
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }
    }
}
