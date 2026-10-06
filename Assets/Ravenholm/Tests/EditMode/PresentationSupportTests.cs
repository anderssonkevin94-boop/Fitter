using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Ravenholm.Content;
using Ravenholm.Domain.Logistics;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Infrastructure.Save;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>Phase 8: settings, first-hour prompts, schema 9, icons and the catch-up budget.</summary>
    public sealed class PresentationSupportTests : TempDirFixture
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

        // ------------------------------------------------------------------ settings

        [Test]
        public void SettingsPersistSeparatelyAndSurviveADamagedFile()
        {
            var dir = Path.Combine(Dir, "settings");
            var a = new PlayerSettingsService(dir);
            Assert.IsTrue(a.Current.Haptics);
            a.Update(s => { s.Haptics = false; s.ReducedMotion = true; s.TextScale = 1.3f; s.MusicVolume = 0f; s.JoystickSide = JoystickSide.Left; });
            var b = new PlayerSettingsService(dir);
            Assert.IsFalse(b.Current.Haptics);
            Assert.IsTrue(b.Current.ReducedMotion);
            Assert.AreEqual(1.3f, b.Current.TextScale);
            Assert.AreEqual(0f, b.Current.MusicVolume);
            Assert.AreEqual(JoystickSide.Left, b.Current.JoystickSide);

            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ not json");
            var c = new PlayerSettingsService(dir);
            Assert.IsTrue(c.Current.Haptics, "a damaged settings file falls back to defaults");

            b.Update(s => { s.TextScale = 7f; s.EffectsVolume = 3f; });
            Assert.AreEqual(1f, b.Current.TextScale, "only offered text sizes");
            Assert.AreEqual(1f, b.Current.EffectsVolume);
        }

        // ------------------------------------------------------------------ first-hour prompts

        [Test]
        public void PromptsFollowTheFirstHourAndLaterStepsFinishEarlierOnes()
        {
            var g = Boot();
            var t = g.Tutorial;
            Assert.AreEqual(12, t.Steps.Count);
            Assert.AreEqual("tutorial.move_and_pick_up", t.Current.Id);

            g.Logistics.RegisterContainer("test.box", ContainerKind.OutputPad, 100f, 100f, 0.45f, null);
            g.Logistics.System.CreateItemInContainer("test.box", "item.iron_sword");
            for (var i = 0; i < 20; i++) g.Logistics.System.StepOnce(100f, 100f);
            Assert.IsTrue(t.Refresh());
            Assert.AreEqual("tutorial.deliver", t.Current.Id);

            g.Logistics.System.DebugUnloadCarry();   // gear now in the Store: steps up to "stock_store" finish together
            t.Refresh();
            Assert.IsTrue(t.IsDone("tutorial.deliver") && t.IsDone("tutorial.craft") && t.IsDone("tutorial.stock_store"));
            Assert.AreEqual("tutorial.post_job", t.Current.Id);

            t.Acknowledge("tutorial.danger");   // an information tip, finished by "Got it"
            Assert.AreEqual("tutorial.blueprint", t.Current.Id);
            g.Scheduler.SaveNow("test");

            var back = Boot();
            Assert.AreEqual("tutorial.blueprint", back.Tutorial.Current.Id, "progress survives a restart");
            back.Tutorial.SetHidden(true);
            back.Scheduler.SaveNow("test");
            Assert.IsNull(Boot().Tutorial.Current, "hidden stays hidden");
        }

        [Test]
        public void PromptsNeverChangeTheGame()
        {
            var g = Boot();
            var before = g.Jobs.State.Gold;
            for (var i = 0; i < 5; i++) { g.Tutorial.Refresh(); g.Tutorial.Acknowledge(g.Tutorial.Current.Id); }
            Assert.AreEqual(before, g.Jobs.State.Gold);
            CollectionAssert.IsEmpty(g.Logistics.System.Audit());
        }

        [Test]
        public void V8SavesMigrateAndReturningPlayersStartWithTipsHidden()
        {
            var json = File.ReadAllText("Assets/Ravenholm/Tests/Fixtures/save_v8_fixture.json");
            Assert.IsTrue(SaveSerializer.TryFromJson(json, out var env, out var from, out var err), err);
            Assert.AreEqual(8, from);
            Assert.AreEqual(SaveEnvelope.CurrentSchemaVersion, env.SchemaVersion);
            Assert.IsTrue(env.Onboarding.Hidden, "the fixture company has quest results: a returning tester");
            Assert.AreEqual(180_000, env.Economy.TraderAnchorMs);

            var fresh = JObject.Parse(json);
            ((JArray)fresh["jobs"]["results"]).Clear();
            Assert.IsTrue(SaveSerializer.TryFromJson(fresh.ToString(), out var env2, out _, out err), err);
            Assert.IsNull(env2.Onboarding, "a company that never finished a job still gets the prompts");

            Assert.IsTrue(new SaveStore(Dir).Write(json, env.SaveGeneration, out err), err);
            var g = new GameServices(new ManualClock(env.LastAcceptedUtc), Cat, Balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            Assert.IsNull(g.Tutorial.Current);
            Assert.AreEqual(73, g.Jobs.State.Gold, "Phase 7 era saves keep their state");
        }

        // ------------------------------------------------------------------ art coverage and budgets

        [Test]
        public void EveryResourceAndItemHasAnIconAndStackSprite()
        {
            foreach (var r in Cat.Resources)
            {
                Assert.NotNull(r.Icon, r.Id + " icon");
                Assert.NotNull(r.StackSprite, r.Id + " stack sprite");
            }
            foreach (var i in Cat.Items)
            {
                Assert.NotNull(i.Icon, i.Id + " icon");
                Assert.NotNull(i.StackSprite, i.Id + " stack sprite");
            }
            var provenance = File.ReadAllText("Assets/Ravenholm/Art/Placeholder/PROVENANCE.md");
            StringAssert.Contains("prop_beacon_trophy", provenance);
            StringAssert.Contains("prop_pennant", provenance);
        }

        [Test]
        public void EightHourCatchUpStaysWithinTheBudget()
        {
            var g = Boot();
            SimulationService.MakeBusyForProfiling(g);
            g.Simulation.Simulate(60_000, "warm-up");
            var sw = Stopwatch.StartNew();
            g.Simulation.Simulate(SimulationService.OfflineResourceCapMs, "budget");
            sw.Stop();
            Assert.Greater(g.Production.System.State.SimMs, 7 * 3_600_000L, "the facilities really ran for the whole cap");
            UnityEngine.Debug.Log($"[Budget] 8 h catch-up in the editor: {sw.ElapsedMilliseconds} ms");
            Assert.Less(sw.ElapsedMilliseconds, 1000, "GDD 24: near 1 s for the normal cap (editor check; device figure comes from PerfProbe)");
        }
    }
}
