using System;
using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Shop;
using Ravenholm.Infrastructure.Save;

namespace Ravenholm.Services
{
    /// <summary>
    /// The player's shop (Shop Titans style, 2026-10-15): owned 2 x 2 blocks and furniture, placed, rotated, put away
    /// and upgraded freely; blocks and new pieces are bought with gold and materials. Owns the "shop" save section.
    /// Every command validates first, then pays and applies together.
    /// </summary>
    public sealed class ShopService : ISaveSection
    {
        private readonly SaveScheduler _scheduler;
        private readonly ContentCatalogue _catalogue;
        private readonly JobsService _jobs;
        private readonly LogisticsService _logistics;
        private readonly ShopLayoutConfig _config = new ShopLayoutConfig();
        private readonly Dictionary<string, FurnitureShape> _shapes = new Dictionary<string, FurnitureShape>();
        private ShopGrid _grid;

        public ShopService(SaveScheduler scheduler, ContentCatalogue catalogue, JobsService jobs, LogisticsService logistics)
        {
            _scheduler = scheduler;
            _catalogue = catalogue;
            _jobs = jobs;
            _logistics = logistics;
            BuildConfig();
            scheduler.Register(this);
        }

        public ShopState State { get; private set; } = new ShopState();
        public ShopDefinition Definition => _catalogue != null ? _catalogue.Shop : null;
        public ShopGrid Grid => _grid ??= new ShopGrid(State, _config, ShapeOf);
        public bool Enabled => Definition != null;

        /// <summary>Raised after any change to the blocks or the furniture.</summary>
        public event Action Changed;

        public FurnitureShape ShapeOf(string furnitureId) => furnitureId != null && _shapes.TryGetValue(furnitureId, out var s) ? s : null;

        public ShopFurnitureDefinition FurnitureOf(Placement p) =>
            p != null && _catalogue.TryGet<ShopFurnitureDefinition>(p.FurnitureId, out var d) ? d : null;

        public Placement Find(string id) => State.Furniture.Find(p => p.Id == id);

        private void BuildConfig()
        {
            var def = Definition;
            if (def == null) return;
            _config.LotA = def.LotBlocks.x;
            _config.LotB = def.LotBlocks.y;
            foreach (var b in def.StartBlocks) _config.StartBlocks.Add(new Tile(b.x, b.y));
            _config.Entrance = new Tile(def.EntranceTile.x, def.EntranceTile.y);
            _config.EntranceSide = def.Entrance == ShopDefinition.EntranceEdge.FrontLeft ? EdgeSide.FrontLeft : EdgeSide.FrontRight;
            foreach (var f in _catalogue.ShopFurniture)
            {
                if (f == null) continue;
                var shape = new FurnitureShape { Id = f.Id, Wall = f.Wall, WallEdges = f.WallEdges };
                foreach (var l in f.Levels) shape.Footprints.Add((l.footprint.x, l.footprint.y));
                _shapes[f.Id] = shape;
            }
        }

        private void Commit(string reason)
        {
            _grid = null;
            _scheduler.SaveNow(reason);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ furniture commands

        public string CanPlace(Placement p, int a, int b, Facing f) => Grid.CanPlace(ShapeOf(p.FurnitureId), p.Level, a, b, f, p);

        /// <summary>Places (or moves) a piece. Returns why not, or null.</summary>
        public string Place(string id, int a, int b, Facing f)
        {
            var p = Find(id);
            if (p == null) return "No such piece";
            var why = CanPlace(p, a, b, f);
            if (why != null) return why;
            p.Placed = true;
            p.A = a;
            p.B = b;
            p.Facing = f;
            Commit("shop: furniture placed");
            return null;
        }

        /// <summary>Turns a piece in place (floor pieces: the other facing; wall pieces: no turning).</summary>
        public string Rotate(string id)
        {
            var p = Find(id);
            if (p == null || !p.Placed) return "Place it first";
            var shape = ShapeOf(p.FurnitureId);
            if (shape == null || shape.Wall) return "Wall pieces hang on one wall";
            return Place(id, p.A, p.B, p.Facing == Facing.A ? Facing.B : Facing.A);
        }

        public string Store(string id)
        {
            var p = Find(id);
            if (p == null) return "No such piece";
            if (!p.Placed) return null;
            p.Placed = false;
            Commit("shop: furniture put away");
            return null;
        }

        public string UpgradeBlockedReason(Placement p)
        {
            var def = FurnitureOf(p);
            if (def == null) return "Unknown furniture";
            if (p.Level >= def.MaxLevel) return "Fully upgraded";
            if (p.Placed)
            {
                // A bigger level must still fit where it stands.
                var shape = ShapeOf(p.FurnitureId);
                var why = Grid.CanPlace(shape, p.Level + 1, p.A, p.B, p.Facing, p);
                if (why != null) return "No room to grow: " + why;
            }
            var next = def.At(p.Level + 1);
            return CostBlockedReason(next.gold, next.materials);
        }

        public string Upgrade(string id)
        {
            var p = Find(id);
            if (p == null) return "No such piece";
            var why = UpgradeBlockedReason(p);
            if (why != null) return why;
            var next = FurnitureOf(p).At(p.Level + 1);
            if (!Pay(next.gold, next.materials, "furniture upgrade")) return "Materials changed; try again";
            p.Level++;
            Commit("shop: furniture upgraded");
            return null;
        }

        public string BuyBlockedReason(ShopFurnitureDefinition def)
        {
            if (def == null || def.Levels.Count == 0) return "Unknown furniture";
            if (!def.ForSale) return "Not for sale";
            var first = def.At(1);
            return CostBlockedReason(first.gold, first.materials);
        }

        /// <summary>Buys a new piece at level 1; it goes into storage, ready to place.</summary>
        public string Buy(ShopFurnitureDefinition def, out Placement bought)
        {
            bought = null;
            var why = BuyBlockedReason(def);
            if (why != null) return why;
            var first = def.At(1);
            if (!Pay(first.gold, first.materials, "furniture bought")) return "Materials changed; try again";
            bought = NewPiece(def.Id, 1);
            Commit("shop: furniture bought");
            return null;
        }

        private Placement NewPiece(string furnitureId, int level)
        {
            var p = new Placement { Id = "furniture." + State.NextId++, FurnitureId = furnitureId, Level = level };
            State.Furniture.Add(p);
            return p;
        }

        // ------------------------------------------------------------------ expansion

        public ShopDefinition.Cost NextBlockCost => Definition.ExpansionCost(State.Expansions);

        public string BuyBlockedReason(int blockA, int blockB)
        {
            var why = Grid.CanBuy(new Tile(blockA, blockB));
            if (why != null) return why;
            var c = NextBlockCost;
            return CostBlockedReason(c.gold, c.materials);
        }

        /// <summary>Buys a 2 x 2 block. Wall pieces whose wall is gone go back into storage.</summary>
        public string BuyBlock(int blockA, int blockB)
        {
            var why = BuyBlockedReason(blockA, blockB);
            if (why != null) return why;
            var c = NextBlockCost;
            if (!Pay(c.gold, c.materials, "shop expansion")) return "Materials changed; try again";
            State.Blocks.Add(new Tile(blockA, blockB));
            State.Expansions++;
            _grid = null;
            Revalidate();
            Commit("shop: expanded");
            return null;
        }

        /// <summary>Puts away every placed piece that no longer fits (after the shell changed).</summary>
        private void Revalidate()
        {
            foreach (var p in State.Furniture)
            {
                if (!p.Placed) continue;
                if (Grid.CanPlace(ShapeOf(p.FurnitureId), p.Level, p.A, p.B, p.Facing, p) == null) continue;
                p.Placed = false;
                _grid = null;
            }
        }

        // ------------------------------------------------------------------ costs

        private string CostBlockedReason(int gold, IReadOnlyList<ResourceCost> materials)
        {
            if (_jobs.State.Gold < gold) return $"Needs {gold} gold (you have {_jobs.State.Gold})";
            foreach (var c in materials)
            {
                var have = _logistics.System.SharedAvailable(c.resource.Id);
                if (have < c.quantity) return $"Needs {c.quantity} {c.resource.DisplayName} (you have {have})";
            }
            return null;
        }

        private bool Pay(int gold, IReadOnlyList<ResourceCost> materials, string reason)
        {
            var pairs = new List<KeyValuePair<string, int>>();
            foreach (var c in materials) pairs.Add(new KeyValuePair<string, int>(c.resource.Id, c.quantity));
            if (pairs.Count > 0 && !_logistics.System.TryConsumeShared(pairs, reason)) return false;
            _jobs.AddGold(-gold);
            return true;
        }

        // ------------------------------------------------------------------ persistence

        void ISaveSection.Write(SaveEnvelope env)
        {
            if (!Enabled) return;
            var d = new ShopSaveData { NextId = State.NextId, Expansions = State.Expansions };
            foreach (var b in State.Blocks) d.Blocks.Add(new[] { b.A, b.B });
            foreach (var p in State.Furniture)
                d.Furniture.Add(new ShopFurnitureSaveData
                {
                    Id = p.Id, Furniture = p.FurnitureId, Level = p.Level, Placed = p.Placed, A = p.A, B = p.B, Facing = p.Facing.ToString(),
                });
            env.Shop = d;
        }

        void ISaveSection.Read(SaveEnvelope env)
        {
            State = new ShopState();
            _grid = null;
            if (!Enabled) return;
            var d = env?.Shop;
            if (d == null) { Fresh(); Changed?.Invoke(); return; }
            foreach (var b in d.Blocks) if (b != null && b.Length == 2) State.Blocks.Add(new Tile(b[0], b[1]));
            State.NextId = Math.Max(1, d.NextId);
            State.Expansions = d.Expansions;
            foreach (var f in d.Furniture)
            {
                if (ShapeOf(f.Furniture) == null) continue;   // furniture no longer in the content: dropped
                var def = _catalogue.Get<ShopFurnitureDefinition>(f.Furniture);
                State.Furniture.Add(new Placement
                {
                    Id = f.Id, FurnitureId = f.Furniture, Level = Math.Max(1, Math.Min(f.Level, def.MaxLevel)), Placed = f.Placed,
                    A = f.A, B = f.B, Facing = f.Facing == "B" ? Facing.B : Facing.A,
                });
            }
            if (State.Blocks.Count == 0) foreach (var b in _config.StartBlocks) State.Blocks.Add(b);
            Revalidate();
            Changed?.Invoke();
        }

        /// <summary>A new shop: the start blocks and the starting furniture where the content places it.</summary>
        private void Fresh()
        {
            foreach (var b in _config.StartBlocks) State.Blocks.Add(b);
            foreach (var s in Definition.StartingFurniture)
            {
                if (s.furniture == null) continue;
                var p = NewPiece(s.furniture.Id, Math.Max(1, s.level));
                var f = s.facingB ? Facing.B : Facing.A;
                if (Grid.CanPlace(ShapeOf(p.FurnitureId), p.Level, s.tile.x, s.tile.y, f, p) != null) continue;   // stays in storage
                p.Placed = true;
                p.A = s.tile.x;
                p.B = s.tile.y;
                p.Facing = f;
                _grid = null;
            }
        }
    }
}
