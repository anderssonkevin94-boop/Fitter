using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Camp;
using Ravenholm.Domain.Jobs;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The crafting expansion, part 3 (Kevin, 2026-10-16): the Kitchen arrives as a kit and cooks food from what the
    /// forest gives (berries, mushrooms); food is a cheap consumable, always eaten on the trip.
    /// </summary>
    public sealed class KitchenTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

        private GameServices Boot()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");
            var g = new GameServices(new ManualClock(T0), cat, balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            return g;
        }

        private static string Make(GameServices g, string item)
        {
            g.Logistics.System.CreateStoreItem("item." + item, "test stock");
            return g.Logistics.System.StoreItems.Last(i => g.Logistics.System.DefinitionOfItem(i) == "item." + item);
        }

        [Test]
        public void TheKitchenArrivesAsAKitAndTheWorkbenchIsTheWorkshop()
        {
            var g = Boot();
            Assert.AreEqual("Workshop", g.Catalogue.Get<StationDefinition>("station.workbench").DisplayName);
            g.Stations.RegisterDefault("station.kitchen", 20, -10, 2, 2);
            Assert.AreEqual(StationSiteMode.Packed, g.Stations.Site("station.kitchen").Mode, "set it up from its kit");
            Assert.IsTrue(g.Catalogue.Recipes.Any(r => r.Station.Id == "station.kitchen" && r.OutputItem.Category == ItemCategory.Food));
            Assert.IsTrue(g.Catalogue.Recipes.Any(r => r.OutputItem.Id == "item.leather_jerkin" && r.Station.Id == "station.workbench"));
        }

        [Test]
        public void FoodFeedsSurvivalAndReadinessAndTheSameDishDoesNotStack()
        {
            var g = Boot();
            var board = g.Jobs.Board;
            var c = g.Jobs.State.Contractors.Values.First(x => !x.Sponsored);
            var stew = Make(g, "mushroom_stew");
            var stew2 = Make(g, "mushroom_stew");
            var pie = Make(g, "hearty_pie");
            var plain = board.StatsWith(c, new Loadout());
            var fed = board.StatsWith(c, new Loadout { Potion1 = stew });
            Assert.AreEqual(3, fed.FoodSurvival - plain.FoodSurvival);
            Assert.AreEqual(2, fed.FoodReadiness - plain.FoodReadiness);
            Assert.AreEqual(fed.FoodSurvival, board.StatsWith(c, new Loadout { Potion1 = stew, Potion2 = stew2 }).FoodSurvival, "the same dish twice does not stack");
            Assert.AreEqual(7, board.StatsWith(c, new Loadout { Potion1 = stew, Potion2 = pie }).FoodSurvival, "two different dishes do");
            var q = board.Quest("quest.roadside_patrol");
            Assert.Greater(board.Forecast(c, new Loadout { Potion1 = stew }, q).Readiness, board.Forecast(c, new Loadout(), q).Readiness, "well fed: readier");
        }

        [Test]
        public void FoodIsAlwaysEatenOnTheTrip()
        {
            var strong = new LoadoutStats { Might = 12, Survival = 10, Insight = 8, WeaponPower = 14, ArmorProtection = 10, FoodSurvival = 2, FoodReadiness = 1 };
            var food = new List<CarriedPotion> { new CarriedPotion { InstanceId = "inst.food", DefinitionId = "item.trail_rations", Food = true } };
            var r = Resolver.Resolve(new ResolverCoefficients(), strong, 5, new List<ThreatSpec>(), food, 42);
            Assert.GreaterOrEqual(r.Outcome, Outcome.Success, "an easy job");
            CollectionAssert.Contains(r.ConsumedPotions, "inst.food", "eaten even when all goes well");
        }

        [Test]
        public void TheForestGivesBerriesAndMushrooms()
        {
            var g = Boot();
            var forest = g.Sites.Get("site.forest");
            var ridge = forest.Config.Grove.Ridge;
            g.Sites.SpeedMultiplier = 50f;
            int felled = 0, cleared = 0;
            for (var s = 18; s < 140 && cleared < 80; s++)   // on up the road past the gate if need be (the world seed varies)
            {
                var (px, py) = ridge.PointAt(ridge.CampS + s);
                for (var k = -26; k <= 26 && cleared < 80; k++)   // the whole width (camp 1's bay narrows the land ahead)
                {
                    var (x, y) = ((int)px + k, (int)py - k);
                    if (!forest.CanChop(x, y) || g.Sites.ToolTooWeak(x, y)) continue;
                    for (var i = 0; i < 4000 && !forest.IsFelled(x, y); i++) g.Sites.PlayerChopCell("site.forest", x, y, 0.5f);
                    g.Logistics.System.DebugUnloadCarry();
                    felled++;
                    if (g.Sites.ClearStump("site.forest", x, y)) cleared++;
                    g.Logistics.System.DebugUnloadCarry();
                }
            }
            Assert.GreaterOrEqual(cleared, 40);
            var log = g.Logistics.System;
            Assert.Greater(log.SharedAvailable("resource.mushroom"), 5, $"mushrooms among {cleared} pulled stumps");
            Assert.Greater(log.SharedAvailable("resource.berries"), 0, $"berries under some of {felled} felled trees");
            CollectionAssert.IsEmpty(log.Audit());
        }
    }
}
