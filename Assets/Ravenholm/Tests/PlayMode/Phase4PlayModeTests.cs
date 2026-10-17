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
    /// <summary>Phase 4 acceptance in the real scene: post → applicants arrive at the Store → meet → loadout → send.</summary>
    public sealed class Phase4PlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-phase4", Guid.NewGuid().ToString("N"));
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

        [UnityTest]
        public IEnumerator PostMeetEquipAndSend()
        {
            yield return Boot();
            GameRoot.Instance.Services.Jobs.Board.Config.CastlePosts = false;   // posts its own job (the original Job Board; in play, the castle posts)
            var s = GameRoot.Instance.Services;
            var hud = GameRoot.Instance.Hud;
            // Stock a starter loadout: craft from the starting stock, carry it to the Store.
            Assert.IsTrue(s.Production.Craft("recipe.iron_sword", out var f), f);
            Assert.IsTrue(s.Production.Craft("recipe.riveted_coat", out f), f);
            s.Production.DebugFinishCrafts();   // crafting is timed (the crafting expansion): done now for the test
            yield return StandAt(Phase1PlayModeTests.Spot("container.forge_pad"), 0.5f);   // the Blacksmith's pickup mat
            yield return StandAt(Phase1PlayModeTests.Spot("receiver.store"), 0.5f);
            Assert.AreEqual(2, s.Logistics.System.StoreItems.Count);
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(new Vector2(0f, -0.5f));

            hud.ShowJobBoard();
            Assert.IsTrue(hud.JobBoard.Post("quest.roadside_patrol"), hud.JobBoard.Message);
            yield return new WaitForSeconds(0.3f);
            yield return Capture.Take("phase4_job_board_posted");
            hud.JobBoard.Close();

            s.Sites.SpeedMultiplier = 20f;   // debug accelerated time: applicants arrive within ~1.5 s
            var crowd = Object.FindAnyObjectByType<ApplicantCrowdView>();
            var end = Time.realtimeSinceStartup + 5f;
            while (crowd.ShownCount < s.Jobs.State.Posting.Applicants.Count && Time.realtimeSinceStartup < end) yield return null;
            s.Sites.SpeedMultiplier = 1f;
            Assert.AreEqual(2, crowd.ShownCount, "two applicants gather at the Store (reputation 20)");
            yield return Capture.Take("phase4_applicants_at_store");

            var app = s.Jobs.State.Posting.Applicants[0];
            hud.ShowLoadout(app.ContractorId);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(hud.Loadout.IsOpen);
            Assert.IsTrue(TouchInputRouter.IsBlocked);
            yield return Capture.Take("phase4_loadout");

            var gold = s.Jobs.State.Gold;
            var reason = s.Jobs.Board.DispatchBlockedReason(app.ContractorId, hud.Loadout.Current);
            if (reason != null) Assert.Fail("cannot send: " + reason);
            hud.Loadout.Send();              // first dispatch asks to confirm (gear is lost on death)
            Assert.IsTrue(hud.Loadout.IsOpen);
            hud.Loadout.Send();
            yield return null;
            Assert.IsFalse(hud.Loadout.IsOpen);
            Assert.AreEqual(gold - app.Fee, s.Jobs.State.Gold, "fee charged once at dispatch");
            Assert.AreEqual(1, s.Jobs.State.Expeditions.Count);
            Assert.AreEqual(ContractorStatus.OnExpedition, s.Jobs.Board.Contractor(app.ContractorId).Status);
            yield return new WaitForSeconds(1f);
            yield return Capture.Take("phase4_departure");
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }
    }
}
