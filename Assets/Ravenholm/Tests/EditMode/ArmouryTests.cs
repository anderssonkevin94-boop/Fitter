using System;
using System.Collections.Generic;
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
    /// <summary>
    /// The crafting expansion, part 1 (Kevin, 2026-10-16): a weapon, an off-hand, a head piece, body armour and two
    /// consumables. Helmets guard against a threat; shields block a share of killing blows; two-handed weapons (mauls,
    /// bows, staves) leave no hand for a shield.
    /// </summary>
    public sealed class ArmouryTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);
        private static readonly ResolverCoefficients K = new ResolverCoefficients();

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

        private static ContractorRecord Of(GameServices g, string classId) =>
            g.Jobs.State.Contractors.Values.First(c => c.ClassId == classId && !c.Sponsored);

        [Test]
        public void ContentHasHelmetsShieldsAndTwoHandedWeapons()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            Assert.AreEqual(4, cat.Items.Count(i => i.Category == ItemCategory.Helmet));
            Assert.AreEqual(3, cat.Items.Count(i => i.Category == ItemCategory.Shield));
            foreach (var two in new[] { "steel_maul", "cedar_bow", "maple_longbow", "ironwood_greatbow", "cedar_staff", "copper_ward_staff" })
                Assert.IsTrue(cat.Get<ItemDefinition>("item." + two).TwoHanded, two);
            Assert.IsFalse(cat.Get<ItemDefinition>("item.moonsilver_focus").TwoHanded, "the focus stays one-handed: a mage may carry a shield");
            Assert.IsFalse(cat.Get<ItemDefinition>("item.iron_sword").TwoHanded);
            Assert.IsTrue(cat.Recipes.Any(r => r.OutputItem.Id == "item.iron_helm"), "the Forge makes helmets");
            Assert.IsTrue(cat.Recipes.Any(r => r.OutputItem.Id == "item.wooden_shield"), "and the Workbench wooden shields");
        }

        [Test]
        public void AHelmetGuardsAndAShieldBlocksButNotWithTwoHands()
        {
            var g = Boot();
            var board = g.Jobs.Board;
            var warrior = Of(g, "class.warrior");
            var sword = Make(g, "iron_sword");
            var maul = Make(g, "steel_maul");
            var helm = Make(g, "iron_helm");
            var shield = Make(g, "iron_shield");
            var coat = Make(g, "riveted_coat");

            var kit = new Loadout { Weapon = sword, OffHand = shield, Head = helm, Armor = coat };
            var s = board.StatsWith(warrior, kit);
            Assert.AreEqual(25, s.BlockChance, "the iron-bound shield blocks 25%");
            Assert.IsTrue(s.Counters.TryGetValue("HeavyBlows", out var guard) && guard >= 3, "the helm guards against heavy blows");
            Assert.AreEqual(6, s.ArmorProtection, "body armour is still the armour");

            var twoHands = new Loadout { Weapon = maul, OffHand = shield, Armor = coat };
            Assert.AreEqual(0, board.StatsWith(warrior, twoHands).BlockChance, "no shield with a maul");
            var best = board.BestAvailableLoadout(warrior, null);
            Assert.AreEqual(maul, best.Weapon, "the strongest weapon");
            Assert.IsNull(best.OffHand, "two hands on the maul: no shield picked");
            Assert.AreEqual(helm, best.Head);
            Assert.AreEqual(coat, best.Armor);
        }

        [Test]
        public void AShieldTurnsSomeDeathsIntoRetreatsAndChangesNothingElse()
        {
            var weak = new LoadoutStats { Might = 4, Survival = 2, Insight = 2, WeaponPower = 4 };
            var shielded = new LoadoutStats { Might = 4, Survival = 2, Insight = 2, WeaponPower = 4, BlockChance = 35 };
            int deaths = 0, blockedDeaths = 0, blocked = 0;
            for (ulong seed = 1; seed <= 2000; seed++)
            {
                var a = Resolver.Resolve(K, weak, 26, new List<ThreatSpec>(), new List<CarriedPotion>(), seed);
                var b = Resolver.Resolve(K, shielded, 26, new List<ThreatSpec>(), new List<CarriedPotion>(), seed);
                if (a.Outcome == Outcome.Death) deaths++;
                if (b.Outcome == Outcome.Death) blockedDeaths++;
                if (b.Blocked) { blocked++; Assert.AreEqual(Outcome.Death, a.Outcome, "only a death is ever blocked"); Assert.AreEqual(Outcome.Failure, b.Outcome); }
                else Assert.AreEqual(a.Outcome, b.Outcome, "otherwise the same roll, the same outcome");
            }
            Assert.Greater(deaths, 200, "a deadly job for this kit");
            Assert.AreEqual(deaths - blocked, blockedDeaths);
            Assert.That(blocked / (double)deaths, Is.InRange(0.25, 0.45), $"about 35% of deaths blocked ({blocked} of {deaths})");
        }

        [Test]
        public void DispatchRefusesAShieldWithATwoHandedWeaponOrGearInTheWrongSlot()
        {
            var g = Boot();
            var board = g.Jobs.Board;
            var bow = Make(g, "cedar_bow");
            var shield = Make(g, "wooden_shield");
            var helm = Make(g, "leather_cap");
            // An applicant for a castle job (the survey: no gear needed).
            var p = CastleTestsHelpers.WaitForAnyApplicant(g);
            var c = board.Contractor(p.Applicants[0].ContractorId);
            var weaponFor = board.CanWield(c, g.Jobs.Content.Item("item.cedar_bow"));
            StringAssert.Contains("non-shield", board.DispatchBlockedReason(c.Id, new Loadout { OffHand = helm }) ?? "");
            StringAssert.Contains("non-helmet", board.DispatchBlockedReason(c.Id, new Loadout { Head = shield }) ?? "");
            if (weaponFor) StringAssert.Contains("two-handed", board.DispatchBlockedReason(c.Id, new Loadout { Weapon = bow, OffHand = shield }) ?? "");
        }
    }

    internal static class CastleTestsHelpers
    {
        /// <summary>Advances until someone the castle sent has arrived at the shop.</summary>
        public static PostingRecord WaitForAnyApplicant(GameServices g)
        {
            for (var i = 0; i < 720; i++)
            {
                var p = g.Jobs.State.Posting;
                if (p != null && p.Applicants.Count > 0 && g.Jobs.Board.HasArrived(p.Applicants[0])) return p;
                g.Simulation.Advance(5_000, 0);
            }
            Assert.Fail("no adventurer arrived within an hour");
            return null;
        }
    }
}
