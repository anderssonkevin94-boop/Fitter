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

        /// <summary>The default shop: blocks 0..2 x 0..1 (6 x 4 tiles), entrance at (1, 0) on its front-right edge.</summary>
        private static (ShopState state, ShopLayoutConfig config) Start()
        {
            var state = new ShopState();
            var config = new ShopLayoutConfig { Entrance = new Tile(1, 0), EntranceSide = EdgeSide.FrontRight };
            for (var a = 0; a < 3; a++)
                for (var b = 0; b < 2; b++)
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
        public void TheStartIsSixByFourWithWallsOnTheBackAndLowWallsInFront()
        {
            var (s, c) = Start();
            var g = Grid(s, c);
            Assert.AreEqual(24, g.Floor.Count());
            var edges = g.Edges.ToList();
            Assert.AreEqual(6 + 4, edges.Count(e => e.Kind == EdgeKind.Tall), "full walls along both back edges");
            Assert.AreEqual(6 + 4 - 1, edges.Count(e => e.Kind == EdgeKind.Low), "low walls along both front edges, less the entrance");
            Assert.AreEqual(1, edges.Count(e => e.Kind == EdgeKind.Entrance));
            Assert.IsTrue(edges.All(e => (e.Kind == EdgeKind.Tall) == (e.Side == EdgeSide.BackLeft || e.Side == EdgeSide.BackRight)));
            var posts = g.Posts();
            Assert.IsTrue(posts.Any(p => p.A == 6 && p.B == 4 && p.Tall), "the back corner");
            Assert.IsTrue(posts.Any(p => p.A == 0 && p.B == 0 && !p.Tall), "the front corner");
            Assert.IsTrue(posts.Any(p => p.A == 1 && p.B == 0) && posts.Any(p => p.A == 2 && p.B == 0), "a post either side of the entrance");
        }

        [Test]
        public void AFullWallInFrontOfFloorFurtherBackBecomesALowWall()
        {
            var (s, c) = Start();
            s.Blocks.Add(new Tile(2, 2));   // an L: a 2 x 2 block behind the right end
            var g = Grid(s, c);
            Assert.AreEqual(28, g.Floor.Count());
            // The back-left edges in front of the new block's left side would hide it: they are low.
            Assert.IsTrue(g.TryEdge(new Tile(3, 3), EdgeSide.BackLeft, out var k) && k == EdgeKind.Low, "inside corner: low");
            Assert.IsTrue(g.TryEdge(new Tile(4, 5), EdgeSide.BackLeft, out k) && k == EdgeKind.Tall, "the new back wall is full");
            Assert.IsTrue(g.TryEdge(new Tile(0, 3), EdgeSide.BackLeft, out k) && k == EdgeKind.Tall, "far from the block: still full");
            Assert.IsFalse(g.TryEdge(new Tile(4, 3), EdgeSide.BackLeft, out _), "joined floor: no wall between");
        }

        [Test]
        public void FurnitureFitsTheFloorAndNeverBlocksTheWay()
        {
            var (s, c) = Start();
            var g = Grid(s, c);
            var desk = Shapes["desk"];
            Assert.IsNull(g.CanPlace(desk, 1, 2, 0, Facing.A));
            Assert.AreEqual("Not on the shop floor", g.CanPlace(desk, 1, 5, 0, Facing.A), "two tiles along A from the last column");
            Assert.AreEqual("Keep the entrance clear", g.CanPlace(desk, 1, 0, 0, Facing.A));
            Put(s, "desk", 2, 0);
            g = Grid(s, c);
            Assert.AreEqual("Something is in the way", g.CanPlace(Shapes["table"], 1, 3, 0, Facing.A));
            Assert.IsNull(g.CanPlace(desk, 1, 2, 0, Facing.B, s.Furniture[0]), "turning in place (1 x 2) fits");
            // Walling the entrance in: a desk across (0,1)-(1,1) and a table on (2,1) leaves (1,0) and (0,0) cut off.
            Put(s, "desk", 0, 1);
            g = Grid(s, c);
            Assert.AreEqual("That would block the way to something", g.CanPlace(Shapes["table"], 3, 2, 1, Facing.A));
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
            Assert.IsNull(g.CanPlace(shelf, 1, 1, 3, Facing.A), "back-left wall, tiles (1,3)-(2,3)");
            Assert.IsNull(g.CanPlace(shelf, 1, 5, 1, Facing.B), "back-right wall, tiles (5,1)-(5,2)");
            Assert.AreEqual("Needs a full wall", g.CanPlace(shelf, 1, 1, 2, Facing.A), "not in the middle of the floor");
            Assert.AreEqual("Needs a full wall", g.CanPlace(shelf, 1, 5, 3, Facing.A), "would run past the wall's end");
            Put(s, "shelf", 1, 3);
            g = Grid(s, c);
            Assert.AreEqual("Something already hangs there", g.CanPlace(shelf, 1, 2, 3, Facing.A));
            Assert.IsNull(g.CanPlace(Shapes["table"], 1, 1, 3, Facing.A), "the floor under a wall piece stays free");
        }

        [Test]
        public void BlocksAreBoughtNextToTheShopInsideTheLot()
        {
            var (s, c) = Start();
            var g = Grid(s, c);
            Assert.IsNull(g.CanBuy(new Tile(0, 2)));
            Assert.IsNull(g.CanBuy(new Tile(3, 0)));
            Assert.AreEqual("Already yours", g.CanBuy(new Tile(1, 1)));
            Assert.AreEqual("Must join the shop", g.CanBuy(new Tile(4, 4)));
            Assert.AreEqual("Outside the lot", g.CanBuy(new Tile(5, 0)));
            Assert.AreEqual(5, g.BuyableBlocks().Count(), "three behind, two to the side");
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
        public void ANewShopIsSixByFourWithItsStartingFurniturePlaced()
        {
            var g = Boot();
            var shop = g.Shop;
            Assert.IsTrue(shop.Enabled);
            Assert.AreEqual(24, shop.Grid.Floor.Count(), "4 x 6 tiles");
            Assert.AreEqual(5, shop.State.Furniture.Count);
            Assert.IsTrue(shop.State.Furniture.All(p => p.Placed), "every starting piece fits where the content puts it");
            CollectionAssert.AreEquivalent(new[] { "furniture.desk", "furniture.display_table", "furniture.armor_stand", "furniture.potion_shelf", "furniture.weapon_rack" },
                shop.State.Furniture.Select(p => p.FurnitureId));
        }

        [Test]
        public void MovingTurningUpgradingAndBuyingAreSavedAndCost()
        {
            var g = Boot();
            Grant(g);
            var shop = g.Shop;
            var table = shop.State.Furniture.First(p => p.FurnitureId == "furniture.display_table");
            Assert.IsNull(shop.Place(table.Id, 3, 1, Facing.A));
            Assert.AreEqual("Keep the entrance clear", shop.Place(table.Id, 1, 0, Facing.A));
            var gold = g.Jobs.State.Gold;
            Assert.IsNull(shop.Upgrade(table.Id));
            Assert.AreEqual(2, table.Level);
            Assert.AreEqual(gold - shop.FurnitureOf(table).At(2).gold, g.Jobs.State.Gold);
            Assert.IsNull(shop.Rotate(table.Id), "1 x 2 turns to 2 x 1 in place");
            Assert.AreEqual(Facing.B, table.Facing);
            Assert.AreEqual("Not for sale", shop.Buy(g.Catalogue.Get<ShopFurnitureDefinition>("furniture.desk"), out _));
            Assert.IsNull(shop.Buy(g.Catalogue.Get<ShopFurnitureDefinition>("furniture.armor_stand"), out var stand));
            Assert.IsFalse(stand.Placed, "bought pieces wait in storage");
            Assert.IsNull(shop.Place(stand.Id, 0, 3, Facing.A));
            Assert.IsNull(shop.Store(table.Id));
            g.Scheduler.SaveNow("test");

            var back = Boot();
            var t2 = back.Shop.Find(table.Id);
            Assert.IsFalse(t2.Placed);
            Assert.AreEqual(2, t2.Level);
            Assert.AreEqual(Facing.B, t2.Facing);
            Assert.IsTrue(back.Shop.Find(stand.Id).Placed);
            Assert.AreEqual(6, back.Shop.State.Furniture.Count);
            CollectionAssert.IsEmpty(back.Logistics.System.Audit());
        }

        [Test]
        public void ExpandingCostsMoreEachTimeAndPutsAwayPiecesThatLoseTheirWall()
        {
            var g = Boot();
            Grant(g);
            var shop = g.Shop;
            var shelf = shop.State.Furniture.First(p => p.FurnitureId == "furniture.potion_shelf");
            Assert.AreEqual("Must join the shop", shop.BuyBlock(4, 4));
            var first = shop.NextBlockCost.gold;
            var gold = g.Jobs.State.Gold;
            Assert.IsNull(shop.BuyBlock(0, 2), "behind the shelf's wall");
            Assert.AreEqual(gold - first, g.Jobs.State.Gold);
            Assert.Greater(shop.NextBlockCost.gold, first, "the next block costs more");
            Assert.AreEqual(28, shop.Grid.Floor.Count());
            Assert.IsFalse(shelf.Placed, "its wall is gone: the shelf went into storage");
            g.Scheduler.SaveNow("test");
            var back = Boot();
            Assert.AreEqual(28, back.Shop.Grid.Floor.Count(), "the new block is saved");
            Assert.AreEqual(1, back.Shop.State.Expansions);
        }
    }
}
