using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Jobs;
using Ravenholm.Domain.Time;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>Phase 6 in the real scene: Trader stall and panel, upgrades at buildings, crew, time away.</summary>
    public sealed class Phase6PlayModeTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);
        private string _dir;
        private ManualClock _clock;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-phase6", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            _clock = new ManualClock(T0);
            GameRoot.ClockOverride = _clock;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameRoot.Instance != null) Object.Destroy(GameRoot.Instance.gameObject);
            yield return null;
            yield return null;
            GameRoot.SaveDirectoryOverride = null;
            GameRoot.ClockOverride = null;
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

        private static IEnumerator Shutdown()
        {
            GameRoot.Instance.Services.Sandbox.Flush("test quit");
            Object.Destroy(GameRoot.Instance.gameObject);
            yield return null;
            yield return null;
        }

        private static void Stock(params string[] items)
        {
            var s = GameRoot.Instance.Services;
            s.Logistics.RegisterContainer("test.box", Domain.Logistics.ContainerKind.OutputPad, 100f, 100f, 0.45f, null);
            foreach (var i in items) s.Logistics.System.CreateItemInContainer("test.box", i);
            for (var i = 0; i < 20; i++) s.Logistics.System.StepOnce(100f, 100f);
            s.Logistics.System.DebugUnloadCarry();
        }

        [UnityTest]
        public IEnumerator TraderStallAppearsAndTradesAtThePad()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var hud = GameRoot.Instance.Hud;
            StringAssert.Contains("PHASE 6", hud.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).First(t => t.text.Contains("PHASE")).text);
            var pad = GameObject.Find("Trader Pad").GetComponent<TraderInspectable>();
            var stall = pad.transform.Find("Stall").gameObject;
            yield return null;
            Assert.IsFalse(stall.activeSelf, "no Trader before the first ordinary return");

            SandboxSceneController.Current.Inspect(pad);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(hud.TraderPanel.IsOpen);
            hud.TraderPanel.Close();

            Stock("item.iron_sword", "item.riveted_coat", "item.iron_dagger");
            s.Economy.DebugSummonTrader();
            yield return null;
            yield return null;
            Assert.IsTrue(stall.activeSelf, "the stall shows while the Trader is in town");
            StringAssert.Contains("Trader", hud.ToastText);

            s.Jobs.DebugSetGold(40);
            SandboxSceneController.Current.Inspect(pad);
            yield return new WaitForSeconds(0.4f);
            yield return Capture.Take("phase6_trader_panel");
            var ingots = s.Logistics.System.SharedAvailable("resource.iron_ingot");
            Assert.IsTrue(hud.TraderPanel.Buy("iron_bundle"), hud.TraderPanel.LastMessage);
            Assert.AreEqual(37, s.Jobs.State.Gold);
            Assert.AreEqual(ingots + 1, s.Logistics.System.SharedAvailable("resource.iron_ingot"));
            var dagger = s.Logistics.System.StoreItems.First(i => s.Logistics.System.DefinitionOfItem(i) == "item.iron_dagger");
            Assert.IsTrue(hud.TraderPanel.Sell(dagger), hud.TraderPanel.LastMessage);
            Assert.Greater(s.Jobs.State.Gold, 37);
            hud.TraderPanel.Close();
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }

        [UnityTest]
        public IEnumerator UpgradesAtTheBuildingAndCrewAppearsInTown()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var hud = GameRoot.Instance.Hud;
            s.Jobs.DebugSetGold(400);
            foreach (var (id, q) in new[] { ("iron_ingot", 30), ("cedar_lumber", 30) }) s.Logistics.System.ProduceShared("resource." + id, q, "test grant");

            // Lumbermill 2 needs Forest area 2 revealed (test fixture).
            var forest = s.Sites.Get("site.forest").Record;
            forest.Revealed[2] = forest.Composition;
            SandboxSceneController.Current.Inspect(GameObject.Find("Lumbermill").GetComponent<StationInspectable>());
            yield return new WaitForSeconds(0.4f);
            yield return Capture.Take("phase6_station_upgrade");
            var up = s.Catalogue.Get<UpgradeDefinition>("upgrade.lumbermill_2");
            Assert.IsTrue(hud.StationPanel.BuyUpgrade(up), hud.StationPanel.LastMessage);
            Assert.AreEqual(2, s.Production.System.Level("station.lumbermill"));
            hud.StationPanel.Close();

            var mara = GameObject.Find("Mara Quill");
            Assert.NotNull(mara);
            yield return null;
            Assert.IsFalse(mara.transform.Find("Visual").gameObject.activeSelf, "recruit hidden until hired");

            s.Jobs.State.Progress["quest.roadside_patrol"] = new QuestProgress { Attempts = 2, Successes = 2 };   // test fixture
            hud.ShowSite("site.mine");
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(hud.SitePanel.BuyUpgrade(s.Catalogue.Get<UpgradeDefinition>("upgrade.mine_equipment_2")), hud.SitePanel.LastMessage);
            Assert.IsTrue(hud.SitePanel.BuyEquipment("worker.dagna", s.Catalogue.Get<WorkerEquipmentDefinition>("equipment.tool_iron")), hud.SitePanel.LastMessage);
            Assert.IsTrue(hud.SitePanel.BuyUpgrade(s.Catalogue.Get<UpgradeDefinition>("upgrade.mine_crew")), hud.SitePanel.LastMessage);
            yield return new WaitForSeconds(0.4f);
            yield return Capture.Take("phase6_site_crew");
            hud.SitePanel.Close();
            yield return null;
            Assert.IsTrue(mara.transform.Find("Visual").gameObject.activeSelf, "recruit appears once hired");
            Assert.AreEqual(2, s.Sites.Get("site.mine").Record.Workers.Count);
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }

        [UnityTest]
        public IEnumerator ReturningAfterHoursShowsTheAwaySummaryOnce()
        {
            yield return Boot();
            Assert.IsFalse(GameRoot.Instance.Hud.AwayPanel.IsOpen, "fresh company: nothing to summarise");
            yield return Shutdown();

            _clock.Set(T0.AddHours(3));
            yield return Boot();
            var hud = GameRoot.Instance.Hud;
            yield return null;
            Assert.IsTrue(hud.AwayPanel.IsOpen, "summary shown on return");
            StringAssert.Contains("3 h", hud.AwayPanel.BodyText);
            yield return new WaitForSeconds(0.2f);
            yield return Capture.Take("phase6_away_summary");
            hud.AwayPanel.Close();
            Assert.IsNull(GameRoot.Instance.Services.Simulation.PendingReport);
            yield return Shutdown();

            yield return Boot();   // no time passed since
            Assert.IsFalse(GameRoot.Instance.Hud.AwayPanel.IsOpen);
        }
    }
}
