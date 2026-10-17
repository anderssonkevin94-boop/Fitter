using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Infrastructure.Save;
using Ravenholm.Presentation;
using Ravenholm.Services;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Kevin, 2026-10-18: tapping a display (table, stand, rack) opens a picker to choose what it shows, like equipping a
    /// hero. Chosen spots show their item; the rest fill in the racks' order without repeating it; Auto gives a spot
    /// back; the choice is saved by item id; inventory is untouched.
    /// </summary>
    public sealed class DisplayPickerPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-display", Guid.NewGuid().ToString("N"));
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

        [UnityTest]
        public IEnumerator TappingADisplayOpensAPickerAndTheChosenItemShows()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            for (var i = 0; i < 3; i++) yield return null;
            var root = GameRoot.Instance;
            root.Hud.Tips.HideAll();
            var s = root.Services;

            // A display with two or more spots, and three item types it can show, one of each in the Store.
            var view = ShopView.Current.Views.Values.FirstOrDefault(v => v.Definition.Display != FurnitureDisplay.None && !v.LayeredStand && v.SlotCount >= 2);
            Assert.IsNotNull(view, "the camp has a display with two spots or more");
            var fits = s.Catalogue.Items.Where(i => i != null && view.Definition.Shows(i) && i.StackSprite != null).Take(3).ToList();
            Assert.GreaterOrEqual(fits.Count, 2, $"{view.Definition.DisplayName} can show two item types");
            foreach (var item in fits) s.Logistics.System.CreateStoreItem(item.Id, "test stock");
            var stockBefore = s.Logistics.System.State.StoreItems.Count;
            yield return new WaitForSeconds(0.3f);

            // Tapping it opens the picker (not an info card).
            var tap = view.GetComponent<DisplayInspectable>();
            Assert.IsNotNull(tap, "a display piece is tapped to arrange it");
            SandboxSceneController.Current.Inspect(tap);
            yield return new WaitForSeconds(0.3f);
            var panel = root.Hud.Display;
            Assert.IsTrue(panel.IsOpen, "the picker opens");
            Assert.IsFalse(root.Hud.IsInfoOpen, "not the info card");
            Assert.AreEqual(view.SlotCount, panel.SpotCount);

            // Auto first: whatever the racks put in spot 1. Choose another of the stocked types there.
            var auto0 = view.CurrentStock[0].def;
            Assert.IsNotNull(auto0, "Auto fills spot 1");
            var other = fits.First(i => i != auto0);
            panel.SelectSpot(0);
            Assert.IsTrue(panel.Choose(other.Id), "choosing a stocked item that fits");
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(other, view.CurrentStock[0].def, "spot 1 shows the chosen item");
            Assert.AreEqual(1, view.CurrentStock.Count(x => x.def == other), "and it isn't repeated in an Auto spot");
            Assert.IsTrue(view.CurrentStock.Any(x => x.def == auto0), "the item it displaced moves to another spot");
            Assert.AreEqual(stockBefore, s.Logistics.System.State.StoreItems.Count, "inventory is untouched");
            yield return Capture.Take("display_picker");

            // Saved by item id.
            var env = new SaveEnvelope();
            ((ISaveSection)s.Shop).Write(env);
            Assert.AreEqual(other.Id, env.Shop.Furniture.Single(f => f.Id == view.PlacementId).Shown[0], "the choice is saved by item id");

            // An item this piece can't show is refused with a reason.
            var wrong = s.Catalogue.Items.FirstOrDefault(i => i != null && !view.Definition.Shows(i));
            if (wrong != null) Assert.IsFalse(panel.Choose(wrong.Id), "an item that doesn't fit is refused");

            // Auto gives the spot back to the racks' order.
            Assert.IsTrue(panel.Choose(null));
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(auto0, view.CurrentStock[0].def, "Auto restores the racks' own pick");
            panel.Close();
            yield return null;
            Assert.IsFalse(panel.IsOpen);
        }
    }
}
