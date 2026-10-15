using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Domain.Jobs;
using Ravenholm.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>Post-roadmap polish in the scene: developer-mode gating, away summary, town signals, blueprint reveal.</summary>
    public sealed class PolishPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-polish", Guid.NewGuid().ToString("N"));
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

        private static bool AnyVisibleText(Transform root, string contains) =>
            root.GetComponentsInChildren<TextMeshProUGUI>(false).Any(t => t.isActiveAndEnabled && t.text.Contains(contains));

        [UnityTest]
        public IEnumerator DebugReadoutsOnlyShowInDeveloperMode()
        {
            yield return Boot();
            var root = GameRoot.Instance;
            var hud = root.Hud;
            root.Settings.Update(s => s.DeveloperMode = false);
            yield return null;
            Assert.IsFalse(AnyVisibleText(hud.transform, "DEBUG SANDBOX"), "no debug banner for players");
            Assert.IsFalse(AnyVisibleText(hud.transform, "Save gen"), "no save readout for players");
            SandboxSceneController.Current.Inspect(GameObject.Find("Forge").GetComponent<StationInspectable>());
            yield return new WaitForSeconds(0.4f);
            Assert.IsFalse(AnyVisibleText(hud.transform, "DEBUG station level"));
            hud.StationPanel.Close();
            hud.ShowSettings();
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(AnyVisibleText(hud.transform, "Developer tools"));
            for (var i = 0; i < 5; i++) hud.SettingsPanel.TapVersion();
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(Feel.DeveloperMode, "five taps unlock developer mode");
            Assert.IsTrue(AnyVisibleText(hud.transform, "Developer tools"));
            hud.SettingsPanel.Close();
            yield return null;
            Assert.IsTrue(AnyVisibleText(hud.transform, "DEBUG SANDBOX"));
        }

        [UnityTest]
        public IEnumerator AwaySummaryCanViewResultsAndBeReopened()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var hud = GameRoot.Instance.Hud;
            s.Jobs.State.Results.Add(new ResultRecord { ExpeditionId = "expedition.test", QuestId = "quest.roadside_patrol", ContractorName = "Test", Outcome = Outcome.Success });
            var report = s.DebugTimeAway(3_600_000);
            report.Results.Add(s.Jobs.State.Results.Last());
            hud.ShowAwayReport(report);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(AnyVisibleText(hud.transform, "Read the result"));
            hud.AwayPanel.ViewResults();
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(hud.AwayPanel.IsOpen);
            Assert.IsTrue(hud.Result.IsOpen, "the single result opens directly");
            hud.Result.Continue();
            hud.ShowJobBoard();
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(AnyVisibleText(hud.transform, "show the summary again"));
            hud.JobBoard.Close();
        }

        [UnityTest]
        public IEnumerator TownSignalsCraftFlashTraderTimerAndReportMarker()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var signals = Object.FindAnyObjectByType<TownSignals>();
            Assert.NotNull(signals);
            yield return null;
            Assert.AreEqual("", signals.TraderTimerText, "no timer before the Trader is scheduled");
            Assert.IsTrue(s.Production.Craft("recipe.iron_sword", out var f), f);
            Assert.AreEqual(1, signals.Flashes);
            s.Economy.DebugSummonTrader();
            yield return new WaitForSeconds(0.7f);
            StringAssert.StartsWith("Trader here", signals.TraderTimerText);
            Assert.IsFalse(signals.ReportMarkerShown);
            s.Jobs.State.Results.Add(new ResultRecord { ExpeditionId = "expedition.test", QuestId = "quest.roadside_patrol", ContractorName = "Test" });
            yield return null;
            Assert.IsTrue(signals.ReportMarkerShown, "a waiting report is marked at the Store");
            yield return Capture.Take("polish_town_signals");
        }

        [UnityTest]
        public IEnumerator BlueprintResultShowsTheRevealCard()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            s.Jobs.State.Results.Add(new ResultRecord
            {
                ExpeditionId = "expedition.bp", QuestId = "quest.briar_track", ContractorName = "Elin Moss", Outcome = Outcome.Success,
                BlueprintGranted = "blueprint.maple_longbow",
            });
            GameRoot.Instance.Hud.ShowResult("expedition.bp");
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(AnyVisibleText(GameRoot.Instance.Hud.transform, "Craft it at the"));
            yield return Capture.Take("polish_blueprint_reveal");
        }
    }
}
