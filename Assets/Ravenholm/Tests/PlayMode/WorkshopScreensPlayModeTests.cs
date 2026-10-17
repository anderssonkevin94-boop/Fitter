using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    public sealed class WorkshopScreensPlayModeTests
    {
        string _directory;
        [UnitySetUp] public IEnumerator Setup()
        {
            _directory = Path.Combine(Path.GetTempPath(), "ravenholm-screens", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _directory;
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            yield return null;
            var until = Time.realtimeSinceStartup + 30;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox") && Time.realtimeSinceStartup < until) yield return null;
            Assert.NotNull(SandboxSceneController.Current);
            GameRoot.Instance.Settings.Update(s => { s.TextScale = 1.3f; s.DeveloperMode = false; });
            GameRoot.Instance.Hud.Tips.HideAll();
            yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (GameRoot.Instance != null) Object.Destroy(GameRoot.Instance.gameObject);
            yield return null; yield return null;
            GameRoot.SaveDirectoryOverride = null;
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
        static void AssertFooter(Component panel, string action)
        {
            var card = (RectTransform)panel.transform.Find("Card");
            var button = card.Find("Footer/" + action).GetComponent<Button>();
            Assert.IsTrue(button.IsActive());
            Assert.IsFalse(button.transform.IsChildOf(card.Find("Viewport")));
            var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
            foreach (var corner in corners) Assert.IsTrue(card.rect.Contains(card.InverseTransformPoint(corner)), "footer remains inside the frame");
            button.onClick.Invoke();
        }
        [UnityTest] public IEnumerator PinnedActionsCloseScrolledScreensAtLargeText()
        {
            var root = GameRoot.Instance; var hud = root.Hud;
            var screens = new (string name, Action open, Component panel, Func<bool> isOpen)[] {
                ("jobs", hud.ShowJobBoard, hud.JobBoard, () => hud.JobBoard.IsOpen),
                ("settings", hud.ShowSettings, hud.SettingsPanel, () => hud.SettingsPanel.IsOpen),
                ("store", hud.ShowStore, hud.StorePanel, () => hud.StorePanel.IsOpen),
                ("trader", hud.ShowTrader, hud.TraderPanel, () => hud.TraderPanel.IsOpen)
            };
            foreach (var screen in screens)
            {
                screen.open(); yield return new WaitForSeconds(.4f);
                yield return Capture.Take("all_ui_short_" + screen.name, 750, 1334);
                foreach (var scroll in screen.panel.GetComponentsInChildren<ScrollRect>()) scroll.verticalNormalizedPosition = 0;
                yield return null; Canvas.ForceUpdateCanvases();
                AssertFooter(screen.panel, "Close");
                Assert.IsFalse(screen.isOpen());
            }
            Assert.IsFalse(TouchInputRouter.IsBlocked);
            hud.ShowAwayReport(root.Services.Simulation.Simulate(3600000L, "UI test"));
            yield return Capture.Take("all_ui_short_away", 750, 1334);
            AssertFooter(hud.AwayPanel, "Close");
            Assert.IsNull(root.Services.Simulation.PendingReport);
        }
        [UnityTest] public IEnumerator TradeCardsBuySellAndRebuildWithoutDuplicatingActions()
        {
            var root = GameRoot.Instance; var s = root.Services; var hud = root.Hud;
            s.Jobs.DebugSetGold(100); s.Economy.DebugSummonTrader();
            s.Logistics.System.CreateStoreItem("item.iron_sword", "UI trade test");
            hud.ShowTrader(); yield return new WaitForSeconds(.4f);
            var gold = s.Jobs.State.Gold;
            var buy = hud.TraderPanel.GetComponentsInChildren<Button>().First(b => b.name == "Buy" && b.interactable);
            buy.onClick.Invoke(); yield return new WaitForSeconds(.3f);
            Assert.Less(s.Jobs.State.Gold, gold);
            Assert.AreEqual("Bought.", hud.TraderPanel.LastMessage);
            Assert.AreEqual(1, hud.TraderPanel.transform.Find("Card/Footer").childCount);
            var sell = hud.TraderPanel.GetComponentsInChildren<Button>().First(b => b.name == "Sell");
            gold = s.Jobs.State.Gold; sell.onClick.Invoke(); yield return new WaitForSeconds(.3f);
            Assert.Greater(s.Jobs.State.Gold, gold);
            Assert.IsFalse(s.Logistics.System.StoreItems.Any());
            yield return Capture.Take("all_ui_short_trader_stock", 750, 1334);
            AssertFooter(hud.TraderPanel, "Close");
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }
        [UnityTest] public IEnumerator FurnitureCatalogueScrollsAndTravelAndTipsFit()
        {
            var root = GameRoot.Instance; var hud = root.Hud;
            hud.ToggleShopBuild(); yield return null;
            Assert.IsTrue(hud.ShopBuild.Press("Furniture")); yield return new WaitForSeconds(.3f);
            var scroll = hud.ShopBuild.GetComponentInChildren<ScrollRect>();
            Assert.Greater(scroll.content.rect.height, scroll.viewport.rect.height);
            yield return Capture.Take("all_ui_short_build", 750, 1334);
            scroll.verticalNormalizedPosition = 0; yield return null;
            Assert.IsTrue(hud.ShopBuild.Press("Back"));
            hud.ShopBuild.transform.Find("Content/CloseBuild").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(hud.ShopBuild.IsOpen);
            hud.ShowMoveOn(); yield return Capture.Take("all_ui_short_travel", 750, 1334);
            Assert.IsTrue(hud.MoveOn.Press("Not yet")); Assert.IsFalse(hud.MoveOn.IsOpen);
            root.Services.Tutorial.Restart(); yield return new WaitForSeconds(.5f);
            yield return Capture.Take("all_ui_short_tips", 750, 1334);
            var card = (RectTransform)hud.Tips.transform;
            foreach (var button in hud.Tips.GetComponentsInChildren<Button>())
            {
                var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach (var corner in corners) Assert.IsTrue(card.rect.Contains(card.InverseTransformPoint(corner)), button.name + " inside tip card");
            }
        }
    }
}
