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
    /// <summary>Polish batch 3: drawn quest map, Forest depths, character animation.</summary>
    public sealed class PolishBatch3PlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-polish3", Guid.NewGuid().ToString("N"));
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
        public IEnumerator QuestMapDrawsReachedRegionsWithMarkersAndExplainsNodes()
        {
            yield return Boot();
            var hud = GameRoot.Instance.Hud;
            hud.ShowJobBoard();
            yield return new WaitForSeconds(0.4f);
            var nodes = hud.JobBoard.MapNodes;
            Assert.AreEqual(8, nodes.Count, "only Frontier Paths is reached");
            Assert.Greater(nodes.Max(n => n.Position.x) - nodes.Min(n => n.Position.x), 300f, "nodes spread across the map, not bunched");
            Assert.AreEqual(nodes.Count, nodes.Select(n => n.Position).Distinct().Count(), "no two nodes on the same spot");
            Assert.AreEqual(">", nodes.Single(n => n.Quest.Id == "quest.roadside_patrol").Marker, "the campaign's next job");
            Assert.AreEqual("o", nodes.Single(n => n.Quest.Id == "quest.local_supply_survey").Marker);
            Assert.AreEqual("x", nodes.Single(n => n.Quest.Id == "quest.briar_track").Marker);
            hud.JobBoard.ExplainNode(nodes.Single(n => n.Quest.Id == "quest.briar_track").Quest);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(hud.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Any(t => t.text.Contains("Succeed at Roadside Patrol")));
            yield return Capture.Take("polish3_quest_map");
            hud.JobBoard.Close();
        }

        [UnityTest]
        public IEnumerator DeepForestShowsIronwoodAndDarkerAreasFurtherLeft()
        {
            yield return Boot();
            var forest = Object.FindAnyObjectByType<ForestGridView>();
            var cedar = forest.SpriteFor("resource.cedar_log");
            var ironwood = forest.SpriteFor("resource.ironwood_log");
            Assert.NotNull(ironwood);
            Assert.AreNotEqual(cedar, ironwood, "ironwood has its own tree");
            yield return null;
            Assert.AreEqual(1, forest.ShownDepth);
            var sim = GameRoot.Instance.Services.Sites.Get("site.forest");
            Assert.Less(forest.AreaTint(sim, 5).r, forest.AreaTint(sim, 1).r, "areas further left are darker");
        }

        [UnityTest]
        public IEnumerator PlayerReachesApplicantsGreetAndContractorsWalkBack()
        {
            yield return Boot();
            GameRoot.Instance.Services.Jobs.Board.Config.CastlePosts = false;   // posts its own job (the original Job Board; in play, the castle posts)
            var s = GameRoot.Instance.Services;
            var c = SandboxSceneController.Current;
            var motor = c.Player.GetComponent<PlayerMotor>();
            c.Inspect(Object.FindObjectsByType<Inspectable>(FindObjectsInactive.Exclude).First(i => i.transform != c.Player && !i.OpensOwnPanel));
            Assert.IsTrue(motor.Reaching, "the player reaches toward what they used");
            GameRoot.Instance.Hud.HideInfo();

            foreach (var i in new[] { "item.iron_sword", "item.riveted_coat", "item.cedar_bow", "item.iron_dagger", "item.cedar_staff" }) s.Logistics.System.CreateStoreItem(i, "fixture");
            Assert.IsTrue(s.Jobs.Post("quest.roadside_patrol", out var f), f);
            s.Jobs.Board.Advance(31_000);
            yield return null;
            yield return null;
            var idle = Object.FindObjectsByType<CharacterIdle>(FindObjectsInactive.Exclude);
            Assert.IsNotEmpty(idle, "applicants stand at the Store");
            Assert.IsTrue(idle.Any(x => x.Greeting), "they greet on arrival");

            var crowd = Object.FindAnyObjectByType<ApplicantCrowdView>();
            var a = s.Jobs.State.Posting.Applicants[0];
            var who = s.Jobs.Board.Contractor(a.ContractorId);
            Assert.IsTrue(s.Jobs.Dispatch(who.Id, s.Jobs.Board.BestAvailableLoadout(who, JobBoardSystem.LoadoutOwner), out f, out var e), f);
            s.Jobs.DebugForceNext(Outcome.Success);
            s.Simulation.Advance(e.EndMs - s.Jobs.State.SimMs + 1, 0);
            Assert.AreEqual(1, crowd.Returning, "a returning contractor walks back up the road");
        }
    }
}
