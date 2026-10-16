using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Shop;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The modular shop's grid (Shop Titans style, 2026-10-15): the shell drawn from the floor's edges, furniture
    /// placement rules, and 2 x 2 expansion.
    /// </summary>
    public sealed class ShopGridTests
    {
        private static readonly Dictionary<string, FurnitureShape> Shapes = new Dictionary<string, FurnitureShape>
        {
            ["desk"] = Shape("desk", false, (2, 1)),
            ["table"] = Shape("table", false, (1, 1), (1, 2), (1, 3)),
            ["shelf"] = Shape("shelf", true, (1, 1)),
        };

        private static FurnitureShape Shape(string id, bool wall, params (int a, int b)[] fps)
        {
            var s = new FurnitureShape { Id = id, Wall = wall };
            s.Footprints.AddRange(fps);
            return s;
        }

        /// <summary>The default shop: blocks 0..2 x 0..2 (6 x 6 tiles), a 2-tile entrance at (1, 0)-(2, 0) on its front-right edge.</summary>
        private static (ShopState state, ShopLayoutConfig config) Start()
        {
            var state = new ShopState();
            var config = new ShopLayoutConfig { EntranceSide = EdgeSide.FrontRight };
            config.Entrance.Add(new Tile(1, 0));
            config.Entrance.Add(new Tile(2, 0));
            for (var a = 0; a < 3; a++)
                for (var b = 0; b < 3; b++)
                {
                    config.StartBlocks.Add(new Tile(a, b));
                    state.Blocks.Add(new Tile(a, b));
                }
            return (state, config);
        }

        private static ShopGrid Grid(ShopState s, ShopLayoutConfig c) => new ShopGrid(s, c, id => Shapes.TryGetValue(id, out var x) ? x : null);

        private static Placement Put(ShopState s, string id, int a, int b, Facing f = Facing.A, int level = 1)
        {
            var p = new Placement { Id = "p" + s.Furniture.Count, FurnitureId = id, Level = level, Placed = true, A = a, B = b, Facing = f };
            s.Furniture.Add(p);
            return p;
        }

        [Test]
        public void TheStartIsSixBySixWithWallsOnTheBackAndLowWallsInFront()
        {
            var (s, c) = Start();
            var g = Grid(s, c);
            Assert.AreEqual(36, g.Floor.Count());
            var edges = g.Edges.ToList();
            Assert.AreEqual(6 + 6, edges.Count(e => e.Kind == EdgeKind.Tall), "full walls along both back edges");
            Assert.AreEqual(6 + 6 - 2, edges.Count(e => e.Kind == EdgeKind.Low), "low walls along both front edges, less the entrance");
            Assert.AreEqual(2, edges.Count(e => e.Kind == EdgeKind.Entrance), "a 2-tile entrance");
            Assert.IsTrue(edges.All(e => (e.Kind == EdgeKind.Tall) == (e.Side == EdgeSide.BackLeft || e.Side == EdgeSide.BackRight)));
            var posts = g.Posts();
            Assert.IsTrue(posts.Any(p => p.A == 6 && p.B == 6 && p.Tall), "the back corner");
            Assert.IsTrue(posts.Any(p => p.A == 0 && p.B == 0 && !p.Tall), "the front corner");
            Assert.IsTrue(posts.Any(p => p.A == 1 && p.B == 0) && posts.Any(p => p.A == 3 && p.B == 0), "a post either side of the entrance");
            Assert.IsFalse(posts.Any(p => p.A == 2 && p.B == 0), "no post in the middle of the doorway");
        }

        [Test]
        public void AFullWallInFrontOfFloorFurtherBackBecomesALowWall()
        {
            var (s, c) = Start();
            s.Blocks.Add(new Tile(2, 3));   // an L: a 2 x 2 block behind the right end
            var g = Grid(s, c);
            Assert.AreEqual(40, g.Floor.Count());
            // The back-left edges in front of the new block's left side would hide it: they are low.
            Assert.IsTrue(g.TryEdge(new Tile(3, 5), EdgeSide.BackLeft, out var k) && k == EdgeKind.Low, "inside corner: low");
            Assert.IsTrue(g.TryEdge(new Tile(4, 7), EdgeSide.BackLeft, out k) && k == EdgeKind.Tall, "the new back wall is full");
            Assert.IsTrue(g.TryEdge(new Tile(0, 5), EdgeSide.BackLeft, out k) && k == EdgeKind.Tall, "far from the block: still full");
            Assert.IsFalse(g.TryEdge(new Tile(4, 5), EdgeSide.BackLeft, out _), "joined floor: no wall between");
        }

        [Test]
        public void FurnitureFitsTheFloorAndNeverBlocksTheWay()
        {
            var (s, c) = Start();
            var g = Grid(s, c);
            var desk = Shapes["desk"];
            Assert.IsNull(g.CanPlace(desk, 1, 3, 0, Facing.A));
            Assert.AreEqual("Not on the shop floor", g.CanPlace(desk, 1, 5, 0, Facing.A), "two tiles along A from the last column");
            Assert.AreEqual("Keep the entrance clear", g.CanPlace(desk, 1, 0, 0, Facing.A));
            Assert.AreEqual("Keep the entrance clear", g.CanPlace(Shapes["table"], 1, 2, 0, Facing.A), "both entrance tiles");
            Put(s, "desk", 3, 0);
            g = Grid(s, c);
            Assert.AreEqual("Something is in the way", g.CanPlace(Shapes["table"], 1, 4, 0, Facing.A));
            Assert.IsNull(g.CanPlace(desk, 1, 3, 0, Facing.B, s.Furniture[0]), "turning in place (1 x 2) fits");
            // Walling the entrance in: desks at (3,0) and (0,1)-(1,1) and a table on (2,1) leave only the doorway and (0,0).
            Put(s, "desk", 0, 1);
            Put(s, "table", 2, 1);
            g = Grid(s, c);
            Assert.AreEqual("That would block the way to something", g.CanPlace(Shapes["table"], 1, 4, 4, Facing.A), "nothing past them can be reached");
        }

        [Test]
        public void TablesGrowAlongBAndTurnToRunAlongA()
        {
            var t = Shapes["table"];
            Assert.AreEqual((1, 3), t.Footprint(3, Facing.A));
            Assert.AreEqual((3, 1), t.Footprint(3, Facing.B));
            Assert.AreEqual((1, 3), t.Footprint(9, Facing.A), "levels past the last use the last footprint");
        }

        [Test]
        public void WallPiecesHangOnFullWallsOnly()
        {
            var (s, c) = Start();
            var g = Grid(s, c);
            var shelf = Shapes["shelf"];
            Assert.IsNull(g.CanPlace(shelf, 1, 1, 5, Facing.A), "back-left wall, tiles (1,5)-(2,5)");
            Assert.IsNull(g.CanPlace(shelf, 1, 5, 2, Facing.B), "back-right wall, tiles (5,2)-(5,3)");
            Assert.AreEqual("Needs a full wall", g.CanPlace(shelf, 1, 1, 2, Facing.A), "not in the middle of the floor");
            Assert.AreEqual("Needs a full wall", g.CanPlace(shelf, 1, 5, 5, Facing.A), "would run past the wall's end");
            Put(s, "shelf", 1, 5);
            g = Grid(s, c);
            Assert.AreEqual("Something already hangs there", g.CanPlace(shelf, 1, 2, 5, Facing.A));
            Assert.IsNull(g.CanPlace(Shapes["table"], 1, 1, 5, Facing.A), "the floor under a wall piece stays free");
        }

        [Test]
        public void BlocksAreBoughtNextToTheShopInsideTheLot()
        {
            var (s, c) = Start();
            var g = Grid(s, c);
            Assert.IsNull(g.CanBuy(new Tile(0, 3)));
            Assert.IsNull(g.CanBuy(new Tile(3, 0)));
            Assert.AreEqual("Already yours", g.CanBuy(new Tile(1, 1)));
            Assert.AreEqual("Must join the shop", g.CanBuy(new Tile(4, 4)));
            Assert.AreEqual("Outside the lot", g.CanBuy(new Tile(5, 0)));
            Assert.AreEqual(6, g.BuyableBlocks().Count(), "three behind, three to the side");
        }
    }

    /// <summary>The shop service: the default shop, purchases, expansion and saving (EditMode, real content).</summary>
    public sealed class ShopServiceTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

        private GameServices Boot()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");
            var g = new GameServices(new ManualClock(T0), cat, balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            return g;
        }

        private static void Grant(GameServices g)
        {
            g.Jobs.DebugSetGold(5000);
            foreach (var r in new[] { "cedar_lumber", "maple_lumber", "iron_ingot", "steel_ingot", "copper_ingot" })
                g.Logistics.System.ProduceShared("resource." + r, 100, "test grant");
        }

        [Test]
        public void ANewShopIsTheWagonsFourByFourCarpetWithItsStartingFurniture()
        {
            var g = Boot();
            var shop = g.Shop;
            Assert.IsTrue(shop.Enabled);
            Assert.IsTrue(shop.Wagon, "the shop folds out of the wagon (the journey, step 3)");
            Assert.AreEqual(1, shop.WagonLevel);
            Assert.AreEqual(16, shop.Grid.Floor.Count(), "a 4 x 4 carpet");
            Assert.IsFalse(shop.Grid.Edges.Any(e => e.Kind == EdgeKind.Tall), "no walls");
            Assert.AreEqual(4, shop.State.Furniture.Count);
            Assert.IsTrue(shop.State.Furniture.All(p => p.Placed), "every starting piece fits where the content puts it");
            CollectionAssert.AreEquivalent(new[] { "furniture.display_table", "furniture.armor_stand", "furniture.weapon_stand", "furniture.potion_stand" },
                shop.State.Furniture.Select(p => p.FurnitureId));
        }

        [Test]
        public void MovingTurningUpgradingAndBuyingAreSavedAndCost()
        {
            var g = Boot();
            Grant(g);
            var shop = g.Shop;
            var table = shop.State.Furniture.First(p => p.FurnitureId == "furniture.display_table");
            Assert.IsNull(shop.Place(table.Id, 1, 1, Facing.A), "anywhere free on the carpet");
            var gold = g.Jobs.State.Gold;
            Assert.IsNull(shop.Upgrade(table.Id));
            Assert.AreEqual(2, table.Level);
            Assert.AreEqual(gold - shop.FurnitureOf(table).At(2).gold, g.Jobs.State.Gold);
            Assert.IsNull(shop.Rotate(table.Id), "1 x 2 turns to 2 x 1 in place");
            Assert.AreEqual(Facing.B, table.Facing);
            Assert.AreEqual("Not for sale", shop.Buy(g.Catalogue.Get<ShopFurnitureDefinition>("furniture.desk"), out _), "the counter replaced the desk");
            Assert.AreEqual("Not for sale", shop.Buy(g.Catalogue.Get<ShopFurnitureDefinition>("furniture.weapon_rack"), out _), "no walls to hang it on");
            Assert.IsNull(shop.Buy(g.Catalogue.Get<ShopFurnitureDefinition>("furniture.long_stand"), out var stand));
            Assert.IsFalse(stand.Placed, "bought pieces wait in storage");
            Assert.AreEqual("Needs a full wall", shop.CanPlace(new Placement { FurnitureId = "furniture.potion_shelf", Level = 1 }, 0, 0, Facing.A));
            Assert.IsNotNull(shop.Place(stand.Id, 2, 2, Facing.A), "it would shut the potion stand and the counter off");
            Assert.IsNull(shop.Store(table.Id));
            Assert.IsNull(shop.Place(stand.Id, 1, 1, Facing.A));   // the front row stays open to the armour stand (3, 0)
            g.Scheduler.SaveNow("test");

            var back = Boot();
            var t2 = back.Shop.Find(table.Id);
            Assert.IsFalse(t2.Placed);
            Assert.AreEqual(2, t2.Level);
            Assert.AreEqual(Facing.B, t2.Facing);
            Assert.IsTrue(back.Shop.Find(stand.Id).Placed);
            Assert.AreEqual(5, back.Shop.State.Furniture.Count);
            CollectionAssert.IsEmpty(back.Logistics.System.Audit());
        }

        [Test]
        public void AWayToTheCounterIsAlwaysKept()
        {
            var g = Boot();
            Grant(g);
            var shop = g.Shop;
            foreach (var p in shop.State.Furniture.ToArray()) shop.Store(p.Id);
            // Fill the carpet's back row (along the counter) with stands: the last one would shut the counter off.
            var stands = new List<Placement>();
            for (var i = 0; i < 2; i++)
            {
                Assert.IsNull(shop.Buy(g.Catalogue.Get<ShopFurnitureDefinition>("furniture.weapon_stand"), out var st));
                stands.Add(st);
            }
            Assert.IsNull(shop.Place(stands[0].Id, 0, 3, Facing.A));
            Assert.AreEqual("Keep a way to the counter", shop.Place(stands[1].Id, 2, 3, Facing.A));
        }

        [Test]
        public void UpgradingTheWagonGrowsTheCarpetAndCosts()
        {
            var g = Boot();
            Grant(g);
            var shop = g.Shop;
            var cost = shop.NextWagonCost.gold;
            var gold = g.Jobs.State.Gold;
            Assert.IsNull(shop.UpgradeWagon());
            Assert.AreEqual(2, shop.WagonLevel);
            Assert.AreEqual(24, shop.Grid.Floor.Count(), "6 x 4");
            Assert.AreEqual(gold - cost, g.Jobs.State.Gold);
            Assert.IsNull(shop.UpgradeWagon());
            Assert.IsNull(shop.UpgradeWagon());
            Assert.AreEqual(48, shop.Grid.Floor.Count(), "8 x 6 at the top");
            Assert.AreEqual("Fully upgraded", shop.UpgradeWagon());
            g.Scheduler.SaveNow("test");
            var back = Boot();
            Assert.AreEqual(4, back.Shop.WagonLevel, "the wagon's level is saved");
            Assert.AreEqual(48, back.Shop.Grid.Floor.Count());
        }

        [Test]
        public void TheCarpetOnlyGrowsOntoClearedGround()
        {
            var g = Boot();
            Grant(g);
            g.Shop.CarpetGround = (a, b) => a >= 4 ? "Clear the trees first" : null;
            StringAssert.Contains("Clear the trees first", g.Shop.WagonUpgradeBlockedReason());
            Assert.IsNotNull(g.Shop.UpgradeWagon());
            Assert.AreEqual(1, g.Shop.WagonLevel);
        }

        [Test]
        public void AWalledShopFromBeforeTheJourneyMovesOntoTheWagon()
        {
            var g = Boot();
            var old = new Infrastructure.Save.ShopSaveData { Layout = 2, NextId = 10 };
            for (var a = 0; a < 3; a++)
                for (var b = 0; b < 3; b++)
                    old.Blocks.Add(new[] { a, b });
            old.Furniture.Add(new Infrastructure.Save.ShopFurnitureSaveData { Id = "furniture.1", Furniture = "furniture.desk", Level = 2, Placed = true, A = 3, B = 0, Facing = "A" });
            old.Furniture.Add(new Infrastructure.Save.ShopFurnitureSaveData { Id = "furniture.2", Furniture = "furniture.potion_shelf", Level = 2, Placed = true, A = 1, B = 5, Facing = "A" });
            old.Furniture.Add(new Infrastructure.Save.ShopFurnitureSaveData { Id = "furniture.3", Furniture = "furniture.display_table", Level = 1, Placed = true, A = 1, B = 3, Facing = "A" });
            var gold = g.Jobs.State.Gold;
            var desk = g.Catalogue.Get<ShopFurnitureDefinition>("furniture.desk");
            ((ISaveSection)g.Shop).Read(new Infrastructure.Save.SaveEnvelope { Shop = old });
            var shop = g.Shop;
            Assert.AreEqual(3, shop.WagonLevel, "a 6 x 6 floor is a level-3 carpet");
            Assert.IsNull(shop.Find("furniture.1"), "the desk is retired");
            Assert.AreEqual(gold + desk.At(2).gold, g.Jobs.State.Gold, "its upgrade refunded");
            var shelf = shop.Find("furniture.2");
            Assert.AreEqual("furniture.potion_stand", shelf.FurnitureId, "the wall shelf stands free now");
            Assert.AreEqual(2, shelf.Level);
            Assert.IsTrue(shelf.Placed, "set out on the carpet");
            Assert.IsTrue(shop.Find("furniture.3").Placed);
            CollectionAssert.IsEmpty(g.Logistics.System.Audit());
        }
    }
}
