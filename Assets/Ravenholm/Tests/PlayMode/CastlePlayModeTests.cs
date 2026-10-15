using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The castle posts the jobs (Kevin, 2026-10-15), in the real scene: an adventurer walks from the castle to the shop,
    /// is met and kitted out with lent gear (no fee), and departs; the castle's board shows the jobs and who is coming.
    /// </summary>
    public sealed class CastlePlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-castle", Guid.NewGuid().ToString("N"));
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
        public IEnumerator AnAdventurerWalksFromTheCastleAndIsKittedOut()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var hud = GameRoot.Instance.Hud;
            Assert.NotNull(GameObject.Find("Castle"), "the castle stands at the end of the road");
            foreach (var id in new[] { "item.iron_sword", "item.riveted_coat" }) s.Logistics.System.CreateStoreItem(id, "test stock");

            // Skip to the castle's first offer: an adventurer sets off for the shop.
            s.Simulation.Advance(s.Jobs.Board.NextCastleOfferDelay(), 0);
            var p = s.Jobs.State.Posting;
            Assert.NotNull(p, "the castle offered a job");
            Assert.IsTrue(p.ByCastle);
            Assert.AreEqual(1, p.Applicants.Count);
            var crowd = Object.FindAnyObjectByType<ApplicantCrowdView>();
            s.Simulation.Advance((p.Applicants[0].ArriveMs - p.CreatedMs) / 2, 0);
            yield return null;
            Assert.AreEqual(1, crowd.ComingCount, "halfway: walking up the road from the castle");
            SandboxSceneController.Current.Player.GetComponent<PlayerMotor>().Teleport(new Vector2(0f, -0.5f));
            yield return new WaitForSeconds(0.3f);
            yield return Capture.Take("castle_adventurer_coming");

            hud.ShowJobBoard();
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(hud.JobBoard.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Any(t => t.text == "Post Job"), "the castle posts; there is nothing to post");
            yield return Capture.Take("castle_jobs_board");
            hud.JobBoard.Close();

            s.Simulation.Advance(p.Applicants[0].ArriveMs - s.Jobs.State.SimMs + 1, 0);
            yield return null;
            yield return null;
            Assert.AreEqual(0, crowd.ComingCount);
            Assert.AreEqual(1, crowd.ShownCount, "arrived at the shop");

            var app = p.Applicants[0];
            hud.ShowLoadout(app.ContractorId);
            yield return new WaitForSeconds(0.3f);
            yield return Capture.Take("castle_kit_out");
            var gold = s.Jobs.State.Gold;
            hud.Loadout.Send();   // the first dispatch asks to confirm (gear is lost on death)
            hud.Loadout.Send();
            yield return null;
            Assert.IsFalse(hud.Loadout.IsOpen, hud.Loadout.Message);
            Assert.AreEqual(gold, s.Jobs.State.Gold, "no fee: the castle pays them");
            Assert.AreEqual(1, s.Jobs.State.Expeditions.Count);
            Assert.AreEqual(s.Jobs.Board.Config.ShopSharePercent, s.Jobs.State.Expeditions[0].ShopSharePercent);
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }
    }
}
