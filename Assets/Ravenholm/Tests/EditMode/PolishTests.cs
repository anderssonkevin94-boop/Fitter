using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Jobs;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>Post-roadmap polish: debug assistance marks, contractor history, profile setting.</summary>
    public sealed class PolishTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);
        private static ContentCatalogue Cat => AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
        private static BalanceProfile Balance => AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");

        private GameServices Boot()
        {
            var g = new GameServices(new ManualClock(T0), Cat, Balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            return g;
        }

        [Test]
        public void DebugAssistanceIsRecordedInTheSaveAndDiagnostics()
        {
            var g = Boot();
            Assert.AreEqual(0, g.Scheduler.Assisted.Count, "a fresh company is unassisted");
            g.Sites.SpeedMultiplier = 1f;
            Assert.AreEqual(0, g.Scheduler.Assisted.Count, "normal speed is not assistance");
            g.Jobs.DebugSetGold(500);
            g.Sites.SpeedMultiplier = 20f;
            g.Jobs.DebugForceNext(Outcome.Success);
            g.Scheduler.SaveNow("test");
            var back = Boot();
            CollectionAssert.AreEquivalent(new[] { "accelerated time", "forced outcome", "set gold" }, back.Scheduler.Assisted.ToList());
            StringAssert.Contains("debug assistance used: accelerated time, forced outcome, set gold", back.DiagnosticReport("test", 2));
        }

        [Test]
        public void ContractorHistoryKeepsTheLatestThreeInjuriesToughestJobAndDeath()
        {
            var g = Boot();
            var board = g.Jobs.Board;
            var who = g.Jobs.State.Contractors.Values.First(c => !c.Sponsored).Id;
            var n = 0;
            void Send(string quest, Outcome o)
            {
                // History bookkeeping only: a minimal expedition through the real completion path (hiring is tested elsewhere).
                var c = board.Contractor(who);
                var q = board.Quest(quest);
                var e = new ExpeditionRecord
                {
                    Id = $"expedition.h{++n}", QuestId = quest, ContractorId = who, StartMs = g.Jobs.State.SimMs, EndMs = g.Jobs.State.SimMs + 1000,
                    Seed = 1, Snapshot = board.StatsWith(c, new Loadout()), Difficulty = q.Difficulty,
                };
                e.Threats.AddRange(q.Threats);
                c.Status = ContractorStatus.OnExpedition;
                g.Jobs.State.Expeditions.Add(e);
                g.Jobs.DebugForceNext(o);
                g.Simulation.Advance(2000, 0);
                if (o == Outcome.PartialInjury) g.Simulation.Advance(11 * 60_000, 0);   // recover
            }
            Send("quest.roadside_patrol", Outcome.Success);
            Send("quest.roadside_patrol", Outcome.PartialInjury);
            Send("quest.briar_track", Outcome.GreatSuccess);
            Send("quest.roadside_patrol", Outcome.Failure);
            var rec = g.Jobs.Board.Contractor(who);
            Assert.AreEqual(3, rec.Recent.Count);
            Assert.AreEqual("Turned back at Roadside Patrol", rec.Recent[0], "newest first");
            Assert.AreEqual(1, rec.Injuries);
            Assert.AreEqual(18, rec.HighestDifficulty);
            CollectionAssert.Contains(rec.Discoveries, "blueprint.maple_longbow");
            Send("quest.roadside_patrol", Outcome.Death);
            StringAssert.Contains("died at Roadside Patrol", rec.DeathNote);

            g.Scheduler.SaveNow("test");
            var back = Boot().Jobs.Board.Contractor(who);
            Assert.AreEqual(rec.Recent, back.Recent);
            Assert.AreEqual(1, back.Injuries);
            Assert.AreEqual(18, back.HighestDifficulty);
            StringAssert.Contains("died at", back.DeathNote);
        }

        [Test]
        public void ProfileSettingOnlyAcceptsTheDeveloperScratchSave()
        {
            var s = new PlayerSettingsService(Path.Combine(Dir, "settings"));
            Assert.IsNull(s.Current.Profile);
            s.Update(x => x.Profile = "debug");
            Assert.AreEqual("debug", new PlayerSettingsService(Path.Combine(Dir, "settings")).Current.Profile);
            s.Update(x => x.Profile = "../somewhere");
            Assert.IsNull(s.Current.Profile, "no arbitrary folders");
        }
    }
}
