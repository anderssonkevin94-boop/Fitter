using System;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>
    /// How often the game saves (the playtester, 2026-10-17: 81 saves in two minutes of walking). Walking and time passing
    /// save every 20 seconds at most; a real event still saves at once.
    /// </summary>
    public sealed class SaveRateTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc);

        [Test]
        public void WalkingAndTimePassingSaveRarelyARealEventAtOnce()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");
            var g = new GameServices(new ManualClock(T0), cat, balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            var gen = g.Saves.Status.Generation;
            // Ten seconds of walking about while time passes, at 10 frames a second.
            for (var i = 0; i < 100; i++)
            {
                g.Sandbox.ReportPlayerPosition(i * 0.05f, 0f);
                g.Simulation.Tick(0.1f);
                g.Sandbox.Tick(0.1f);
            }
            Assert.AreEqual(gen, g.Saves.Status.Generation, "no save for ten seconds of walking and time passing");
            for (var i = 0; i < 120; i++)
            {
                g.Sandbox.ReportPlayerPosition(5f + i * 0.05f, 0f);
                g.Simulation.Tick(0.1f);
                g.Sandbox.Tick(0.1f);
            }
            Assert.AreEqual(gen + 1, g.Saves.Status.Generation, "one save after twenty seconds of it");
            // A real event (pinning an item on the Store's racks) saves within the short debounce.
            var before = g.Saves.Status.Generation;
            g.Logistics.TogglePin("item.iron_sword");
            for (var i = 0; i < 20; i++) g.Sandbox.Tick(0.1f);
            Assert.AreEqual(before + 1, g.Saves.Status.Generation, "a real event is saved promptly");
        }
    }
}
