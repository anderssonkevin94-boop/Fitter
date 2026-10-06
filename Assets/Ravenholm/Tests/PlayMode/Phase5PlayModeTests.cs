using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Domain.Jobs;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>Phase 5: the complete loop in the real scene (gather/craft → hire → equip → quest → result → improve).</summary>
    public sealed class Phase5PlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-phase5", Guid.NewGuid().ToString("N"));
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

        private static IEnumerator WaitUntil(Func<bool> c, float seconds, string what)
        {
            var end = Time.realtimeSinceStartup + seconds;
            while (!c() && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(c(), "timed out: " + what);
        }

        [UnityTest]
        public IEnumerator FullLoopReturnsAResult()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var hud = GameRoot.Instance.Hud;
            Assert.IsTrue(s.Production.Craft("recipe.iron_sword", out var f), f);
            Assert.IsTrue(s.Production.Craft("recipe.riveted_coat", out f), f);
            Assert.IsTrue(s.Production.Craft("recipe.recovery_tonic", out f), f);
            yield return StandAt(new Vector2(-4.3f, -4.0f), 0.5f);
            yield return StandAt(new Vector2(-3.85f, -1.95f), 0.5f);
            yield return StandAt(new Vector2(-0.55f, 2.95f), 0.6f);
            Assert.AreEqual(3, s.Logistics.System.StoreItems.Count);
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(new Vector2(0f, -0.5f));

            Assert.IsTrue(s.Jobs.Post("quest.roadside_patrol", out f), f);
            s.Sites.SpeedMultiplier = 20f;
            yield return WaitUntil(() => s.Jobs.State.Posting.Applicants.All(a => s.Jobs.Board.HasArrived(a)), 5f, "applicants");
            s.Sites.SpeedMultiplier = 1f;
            var app = s.Jobs.State.Posting.Applicants[0];
            hud.ShowLoadout(app.ContractorId);
            yield return new WaitForSeconds(0.3f);
            yield return Capture.Take("phase5_loadout_with_odds");
            hud.Loadout.Send();
            hud.Loadout.Send();
            yield return null;
            Assert.AreEqual(1, s.Jobs.State.Expeditions.Count);
            var gold = s.Jobs.State.Gold;

            s.Sites.SpeedMultiplier = 20f;   // 90 s patrol → ~4.5 s
            yield return WaitUntil(() => s.Jobs.State.Results.Count == 1, 10f, "expedition result");
            s.Sites.SpeedMultiplier = 1f;
            var r = s.Jobs.State.Results[0];
            Assert.IsFalse(r.Acknowledged);
            StringAssert.Contains(r.ContractorName, hud.ToastText);
            Assert.AreEqual(gold + r.GoldEarned, s.Jobs.State.Gold);
            if (r.Outcome >= Outcome.Success) Assert.IsTrue(s.Jobs.Board.IsUnlocked("quest.briar_track"), "first success opens the map");
            if (r.Died) Assert.AreEqual(ContractorStatus.Dead, s.Jobs.Board.Contractor(r.ContractorId).Status);

            hud.ShowResult(r.ExpeditionId);
            yield return new WaitForSeconds(0.3f);
            StringAssert.Contains("Gold earned", hud.Result.BodyText);
            yield return Capture.Take("phase5_result");
            hud.Result.Continue();
            Assert.IsTrue(r.Acknowledged);

            hud.ShowJobBoard();
            yield return new WaitForSeconds(0.3f);
            yield return Capture.Take("phase5_job_board_map");
            hud.JobBoard.Close();
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
            Debug.Log($"[Phase5] Patrol outcome: {r.Outcome} (E {r.E:0.0}, V {r.V:0.0}); {string.Join(" | ", r.Contributors)}");
        }
    }
}
