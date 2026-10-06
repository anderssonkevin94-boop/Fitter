using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Ravenholm.Domain.Jobs;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>Phase 7 in the real scene: the regional map and breadcrumb, and the trophy after the finale.</summary>
    public sealed class Phase7PlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-phase7", Guid.NewGuid().ToString("N"));
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

        [UnityTest]
        public IEnumerator MapShowsRegionsRumourAndBreadcrumb()
        {
            yield return Boot();
            var hud = GameRoot.Instance.Hud;
            hud.ShowJobBoard();
            yield return new WaitForSeconds(0.4f);
            var map = hud.JobBoard.MapText();
            StringAssert.Contains("Frontier Paths", map);
            StringAssert.Contains("Hollow March</b> (unexplored)", map);
            StringAssert.DoesNotContain("Crownward Ruins", map, "only the adjacent region shows as a silhouette");
            StringAssert.Contains("Roadside Patrol", hud.JobBoard.CampaignText());
            yield return Capture.Take("phase7_job_board_regions");
            hud.JobBoard.Close();
        }

        [UnityTest]
        public IEnumerator TrophyAppearsOnceTheFinaleSucceeds()
        {
            yield return Boot();
            var trophy = Object.FindAnyObjectByType<TrophyView>();
            Assert.NotNull(trophy);
            yield return null;
            Assert.IsFalse(trophy.Shown);
            var s = GameRoot.Instance.Services;
            s.Jobs.State.Progress["quest.beacon_expedition"] = new QuestProgress { Attempts = 1, Successes = 1, Unlocked = true };   // test fixture
            yield return null;
            yield return null;
            Assert.IsTrue(trophy.Shown);
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(new Vector2(-2.4f, 1.6f));
            yield return new WaitForSeconds(0.5f);
            yield return Capture.Take("phase7_beacon_trophy");
            GameRoot.Instance.Hud.ShowJobBoard();
            yield return new WaitForSeconds(0.3f);
            StringAssert.Contains("Campaign complete", GameRoot.Instance.Hud.JobBoard.CampaignText());
        }
    }
}
