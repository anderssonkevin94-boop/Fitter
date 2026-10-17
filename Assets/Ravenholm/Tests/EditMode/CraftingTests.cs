using System;
using System.IO;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Kevin's playtest (2026-10-17): helmets and shields couldn't be crafted on a save begun before the armour pack added
    /// their blueprints. Every blueprint a company starts with is known on load, whenever the save began; and the
    /// playtest switch makes crafts finish at once.
    /// </summary>
    public sealed class CraftingTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc);

        private GameServices Boot()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");
            var g = new GameServices(new ManualClock(T0), cat, balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            return g;
        }

        [Test]
        public void AnOlderSaveLearnsTheHelmetAndShieldBlueprints()
        {
            var g = Boot();
            var known = g.Production.System.State.Blueprints;
            foreach (var id in new[] { "blueprint.leather_cap", "blueprint.wooden_shield", "blueprint.iron_helm", "blueprint.iron_shield" })
                Assert.IsTrue(known.Contains(id), $"{id} known from the start");
            // As a save from before the armour pack: those blueprints missing.
            foreach (var id in new[] { "blueprint.leather_cap", "blueprint.wooden_shield", "blueprint.iron_helm", "blueprint.iron_shield" }) known.Remove(id);
            g.Scheduler.SaveNow("an older save");
            var back = Boot();
            foreach (var id in new[] { "blueprint.leather_cap", "blueprint.wooden_shield", "blueprint.iron_helm", "blueprint.iron_shield" })
                Assert.IsTrue(back.Production.System.State.Blueprints.Contains(id), $"{id} known again after loading");
        }

        [Test]
        public void ThePlaytestSwitchMakesCraftsInstant()
        {
            var g = Boot();
            var recipe = g.Production.System.Recipe("recipe.leather_cap");
            Assert.NotNull(recipe);
            Assert.Greater(g.Production.System.EffectiveCraftMs(recipe), 60000, "a real craft takes minutes");
            g.Production.InstantCrafts = true;
            Assert.LessOrEqual(g.Production.System.EffectiveCraftMs(recipe), 1, "the playtest switch: at once");
        }
    }
}
