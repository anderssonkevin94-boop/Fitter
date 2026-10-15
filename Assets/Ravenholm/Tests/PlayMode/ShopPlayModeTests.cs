using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Domain.Shop;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The modular shop in the real scene (Shop Titans style, 2026-10-15): drawn from its grid with the starting
    /// furniture stocked; build mode picks pieces with world taps, moves, turns and upgrades them, and buys a 2 x 2 plot.
    /// </summary>
    public sealed class ShopPlayModeTests
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

        private static FurnitureView Piece(string id) => ShopView.Current.Views.Values.First(v => v.Definition.Id == id);

        [UnityTest]
        public IEnumerator TheShopIsBuiltPlacedAndGrown()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var hud = GameRoot.Instance.Hud;
            var view = ShopView.Current;
            Assert.NotNull(view, "the shop draws itself");
            Assert.AreEqual(5, view.Views.Count, "the starting furniture stands in the shop");
            var shell = view.transform.Find("Shell");
            Assert.AreEqual(12, shell.Cast<Transform>().Count(t => t.name == "Wall"), "full walls along both back edges (6 + 6)");
            Assert.AreEqual(10, shell.Cast<Transform>().Count(t => t.name == "LowWall"), "low walls along the front, less the 2-tile entrance");
            var shelf = Piece("furniture.potion_shelf");
            foreach (var w in shell.Cast<Transform>().Where(t => t.name == "Wall"))
                if (Vector2.Distance(w.GetChild(0).position, shelf.transform.position) < 0.6f)
                    StringAssert.DoesNotContain("window", w.GetComponentInChildren<SpriteRenderer>().sprite.name, "no window behind a shelf");
            foreach (var id in new[] { "item.iron_sword", "item.steel_maul", "item.steel_coat", "item.iron_dagger", "item.steel_stiletto", "item.antidote", "item.strong_tonic" })
                s.Logistics.System.CreateStoreItem(id, "test stock");
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(view.TileCentre(1, 0));
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(2, Piece("furniture.weapon_rack").VisibleCount, "sword and maul");
            Assert.AreEqual(2, Piece("furniture.display_table").VisibleCount, "dagger and stiletto on the level 1 table");
            Assert.AreEqual(1, Piece("furniture.armor_stand").VisibleCount);
            Assert.AreEqual(2, Piece("furniture.potion_shelf").VisibleCount);
            foreach (var sr in Piece("furniture.weapon_rack").GetComponentsInChildren<SpriteRenderer>().Where(r => r.name == "Slot" && r.gameObject.activeInHierarchy))
                StringAssert.StartsWith("item_", sr.sprite.name, "items show Astra's display views");
            yield return Capture.Take("shop_start");

            // Build mode: the Build button opens the panel; plots appear where the shop can grow.
            hud.ToggleShopBuild();
            yield return null;
            Assert.IsTrue(hud.ShopBuild.IsOpen);
            Assert.IsTrue(view.BuildMode);
            Assert.IsTrue(view.transform.Find("Plots").childCount > 0, "buyable plots shown");

            // Tap the table to pick it, then a tile to move it there.
            var table = Piece("furniture.display_table");
            var tableId = table.PlacementId;
            Assert.IsTrue(view.HandleTap(table.GetComponent<BoxCollider2D>().bounds.center));
            Assert.AreEqual(tableId, view.Selected);
            yield return null;
            StringAssert.Contains("Tap the floor", hud.ShopBuild.InfoText, "the panel offers the piece's moves");
            Assert.IsTrue(view.HandleTap(view.TileCentre(3, 2)));
            var p = s.Shop.Find(tableId);
            Assert.AreEqual((3, 2), (p.A, p.B), "moved where tapped");
            Assert.IsTrue(view.HandleTap(view.TileCentre(1, 0)));
            Assert.AreEqual("Keep the entrance clear", view.LastMessage);
            Assert.AreEqual((3, 2), (p.A, p.B), "a refused move leaves it where it was");

            s.Jobs.DebugSetGold(5000);
            foreach (var r in new[] { "cedar_lumber", "maple_lumber", "iron_ingot", "steel_ingot", "copper_ingot" })
                s.Logistics.System.ProduceShared("resource." + r, 100, "test grant");
            yield return null;
            Assert.IsTrue(hud.ShopBuild.Press("Upgrade"), "upgrade from the panel");
            Assert.AreEqual(2, p.Level);
            Assert.IsTrue(hud.ShopBuild.Press("Turn"));
            Assert.AreEqual(Facing.B, p.Facing);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(2, Piece("furniture.display_table").VisibleCount, "the longer table still shows both small pieces");
            Assert.IsFalse(Piece("furniture.display_table").Mirrored, "facing B is drawn (Astra, step 2)");
            // The desk turned: Astra drew its facing B, so it shows that art rather than a mirror image.
            var desk = Piece("furniture.desk");
            view.Select(desk.PlacementId);
            yield return null;
            Assert.IsTrue(hud.ShopBuild.Press("Turn"), view.LastMessage);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(Facing.B, desk.Facing);
            Assert.IsFalse(desk.Mirrored, "desk_1_B is drawn");
            StringAssert.EndsWith("_B", desk.Visual.sprite.name);
            yield return Capture.Take("shop_build_moved");
            Assert.IsTrue(hud.ShopBuild.Press("Done"));
            Assert.IsNull(view.Selected);

            // A plot behind the back-left wall: pick it and buy it.
            Assert.IsTrue(view.HandleTap(view.TileCentre(1, 6)));
            Assert.AreEqual(new Tile(0, 3), view.SelectedPlot);
            yield return null;
            var gold = s.Jobs.State.Gold;
            Assert.IsTrue(hud.ShopBuild.Press("Buy this plot"), hud.ShopBuild.Message);
            Assert.Less(s.Jobs.State.Gold, gold);
            Assert.AreEqual(40, s.Shop.Grid.Floor.Count());
            Assert.IsNull(view.SelectedPlot, "the bought plot is no longer offered");
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(s.Shop.State.Furniture.First(x => x.FurnitureId == "furniture.potion_shelf").Placed, "its wall moved: the shelf is in storage");
            yield return Capture.Take("shop_build_expanded");

            hud.CloseShopBuild();
            yield return null;
            Assert.IsFalse(view.BuildMode);
            Assert.IsNull(SandboxSceneController.TapOverride, "taps go back to the world");
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }
    }
}
