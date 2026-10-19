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
    /// The wagon shop in the real scene (the journey, step 3, 2026-10-15): the carpet with the starting furniture stocked,
    /// the wagon's counter for the Store; build mode moves and upgrades pieces with world taps and upgrades the wagon.
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
        public IEnumerator TheWagonShopIsSetOutPlacedAndGrown()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var hud = GameRoot.Instance.Hud;
            hud.Tips.HideAll();
            var view = ShopView.Current;
            Assert.NotNull(view, "the shop draws itself");
            Assert.IsTrue(s.Shop.Wagon);
            Assert.AreEqual(4, view.Views.Count, "the starting furniture stands on the carpet");
            var shell = view.transform.Find("Shell");
            Assert.AreEqual(16, s.Shop.Grid.Floor.Count(), "a 4 x 4 floor");
            Assert.AreEqual(0, shell.Cast<Transform>().Count(t => t.name == "Carpet" || t.name == "Fringe"), "no carpet (Kevin, 2026-10-19)");
            Assert.AreEqual(0, shell.Cast<Transform>().Count(t => t.name == "Wall" || t.name == "LowWall"), "no walls");
            Assert.NotNull(view.Wagon, "the wagon is parked behind the carpet");
            Assert.NotNull(view.Wagon.GetComponent<StationInspectable>(), "its counter opens the Store");
            Assert.NotNull(view.Wagon.GetComponentInChildren<BellInspectable>(), "and has the bell");
            var store = s.Logistics.System.State.Receivers["receiver.store"];
            Assert.Less(Vector2.Distance(new Vector2(store.X, store.Y), WalkInCart.Current.CustomerGround), 0.1f, "the Store's marker is at the walk-in wagon's customer spot");
            foreach (var id in new[] { "item.iron_sword", "item.steel_maul", "item.steel_coat", "item.iron_dagger", "item.steel_stiletto", "item.antidote", "item.strong_tonic" })
                s.Logistics.System.CreateStoreItem(id, "test stock");
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(view.TileCentre(1, -1));
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(1, Piece("furniture.weapon_stand").VisibleCount, "the sword: the cradle seats blades only, not the maul");
            Assert.AreEqual(2, Piece("furniture.display_table").VisibleCount, "dagger and stiletto on the level 1 table");
            Assert.AreEqual(1, Piece("furniture.armor_stand").VisibleCount);
            Assert.AreEqual(2, Piece("furniture.potion_stand").VisibleCount);
            yield return Capture.Take("wagon_shop");

            // Build mode: pick the table, move it, upgrade and turn it.
            hud.ToggleShopBuild();
            yield return null;
            Assert.IsTrue(hud.ShopBuild.IsOpen);
            var table = Piece("furniture.display_table");
            var tableId = table.PlacementId;
            Assert.IsTrue(view.HandleTap(table.GetComponent<BoxCollider2D>().bounds.center));
            Assert.AreEqual(tableId, view.Selected);
            Assert.IsTrue(view.HandleTap(view.TileCentre(1, 0)));
            var p = s.Shop.Find(tableId);
            Assert.AreEqual((1, 0), (p.A, p.B), "moved where tapped: the carpet's front edge is fine");
            s.Jobs.DebugSetGold(5000);
            foreach (var r in new[] { "cedar_lumber", "maple_lumber", "iron_ingot", "steel_ingot", "copper_ingot" })
                s.Logistics.System.ProduceShared("resource." + r, 100, "test grant");
            yield return null;
            Assert.IsTrue(hud.ShopBuild.Press("Upgrade"), "upgrade from the panel");
            Assert.AreEqual(2, p.Level);
            Assert.IsTrue(hud.ShopBuild.Press("Done"));

            // The wagon upgrade: a bigger, finer carpet; the wagon and its counter move back with it.
            yield return null;
            var wagonBefore = view.Wagon.position;
            Assert.IsTrue(hud.ShopBuild.Press("Upgrade the wagon"), hud.ShopBuild.Message);
            Assert.AreEqual(2, s.Shop.WagonLevel);
            Assert.AreEqual(24, s.Shop.Grid.Floor.Count());
            yield return new WaitForSeconds(0.3f);
            Assert.AreNotEqual(wagonBefore, view.Wagon.position, "the wagon parks behind the bigger carpet");
            store = s.Logistics.System.State.Receivers["receiver.store"];
            Assert.Less(Vector2.Distance(new Vector2(store.X, store.Y), WalkInCart.Current.CustomerGround), 0.1f, "the marker stays at the customer spot");
            yield return Capture.Take("wagon_shop_upgraded");
            hud.CloseShopBuild();
            yield return null;
            Assert.IsFalse(view.BuildMode);
            Assert.IsNull(SandboxSceneController.TapOverride, "taps go back to the world");
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }
    }
}
