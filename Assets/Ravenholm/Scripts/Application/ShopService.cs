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
            foreach (var e in def.EntranceTiles()) _config.Entrance.Add(new Tile(e.x, e.y));
            _config.EntranceSide = def.Entrance == ShopDefinition.EntranceEdge.FrontLeft ? EdgeSide.FrontLeft : EdgeSide.FrontRight;
            _config.Open = def.Wagon;
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

        /// <summary>The most spots one display piece can hold (the largest potion shelf has 9).</summary>
        public const int MaxDisplaySpots = 16;

        /// <summary>
        /// Chooses what one spot of a display piece shows (2026-10-18, Kevin: pick what a table or stand displays, like
        /// equipping a hero). Null or "" sets the spot back to Auto. Display only: inventory is untouched. Returns why
        /// not, or null.
        /// </summary>
        public string SetShown(string placementId, int spot, string itemDefinitionId)
        {
            var p = Find(placementId);
            var def = FurnitureOf(p);
            if (p == null || def == null) return "No such piece";
            if (def.Display == FurnitureDisplay.None) return "This piece doesn't display goods";
            if (spot < 0 || spot >= MaxDisplaySpots) return "No such spot";
            var id = itemDefinitionId ?? "";
            if (id != "")
            {
                if (!_catalogue.TryGet<ItemDefinition>(id, out var item)) return "No such item";
                if (!def.Shows(item)) return $"{def.DisplayName} can't show {item.DisplayName}";
            }
            while (p.Shown.Count <= spot) p.Shown.Add("");
            if (p.Shown[spot] == id) return null;
            p.Shown[spot] = id;
            while (p.Shown.Count > 0 && p.Shown[p.Shown.Count - 1] == "") p.Shown.RemoveAt(p.Shown.Count - 1);
            Commit("shop: display chosen");
            return null;
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

        // ------------------------------------------------------------------ the wagon (the journey, step 3)

        public bool Wagon => Enabled && Definition.Wagon;
        public int WagonLevel => State.WagonLevel;
        public UnityEngine.Vector2Int CarpetSize => Definition.CarpetSize(State.WagonLevel);

        /// <summary>Why a carpet tile's ground isn't ready (null: cleared, level ground). Set by the scene.</summary>
        public Func<int, int, string> CarpetGround;

        public ShopDefinition.Cost NextWagonCost => Definition.WagonCost(State.WagonLevel + 1);

        public string WagonUpgradeBlockedReason()
        {
            if (!Wagon) return "No wagon";
            if (State.WagonLevel >= Definition.WagonMaxLevel) return "Fully upgraded";
            var next = Definition.CarpetSize(State.WagonLevel + 1);
            if (CarpetGround != null)
                for (var a = 0; a < next.x; a++)
                    for (var b = 0; b < next.y; b++)
                    {
                        if (Grid.IsFloor(a, b)) continue;
                        var why = CarpetGround(a, b);
                        if (why != null) return "Room for the bigger carpet: " + why;
                    }
            var c = NextWagonCost;
            return CostBlockedReason(c.gold, c.materials);
        }

        /// <summary>Upgrades the wagon: a bigger, richer carpet.</summary>
        public string UpgradeWagon()
        {
            var why = WagonUpgradeBlockedReason();
            if (why != null) return why;
            var c = NextWagonCost;
            if (!Pay(c.gold, c.materials, "wagon upgrade")) return "Materials changed; try again";
            SetCarpet(State.WagonLevel + 1);
            Revalidate();
            Commit("shop: wagon upgraded");
            return null;
        }

        private void SetCarpet(int level)
        {
            State.WagonLevel = Math.Max(1, Math.Min(level, Definition.WagonMaxLevel));
            var size = Definition.CarpetSize(State.WagonLevel);
            State.Blocks.Clear();
            for (var a = 0; a < size.x / ShopGrid.BlockSize; a++)
                for (var b = 0; b < size.y / ShopGrid.BlockSize; b++)
                    State.Blocks.Add(new Tile(a, b));
            _grid = null;
        }

        /// <summary>
        /// A walled shop from before the journey moves onto the wagon's carpet: a carpet as big as its floor (6 x 6 is
        /// level 3; any bought block makes it level 4); the desk is retired (its upgrades refunded); wall pieces become
        /// their free-standing versions, at their level.
        /// </summary>
        private void MoveOntoTheWagon()
        {
            SetCarpet(State.Blocks.Count > 9 ? 4 : State.Blocks.Count >= 9 ? 3 : 1);
            foreach (var p in State.Furniture.ToArray())
            {
                var def = FurnitureOf(p);
                if (def == null) continue;
                if (def.RetiredByWagon)
                {
                    for (var l = 2; l <= p.Level; l++)
                    {
                        var paid = def.At(l);
                        _jobs.AddGold(paid.gold);
                        foreach (var m in paid.materials) _logistics.System.ProduceShared(m.resource.Id, m.quantity, "refund: the desk retired");
                    }
                    State.Furniture.Remove(p);
                    continue;
                }
                if (def.Wall && def.StandingVersion != null)
                {
                    p.FurnitureId = def.StandingVersion.Id;
                    p.Level = Math.Min(p.Level, def.StandingVersion.MaxLevel);
                    p.Placed = false;
                }
            }
        }

        /// <summary>Stored pieces go where a new shop has them, then onto the first spot they fit.</summary>
        private void AutoPlace(List<Placement> pieces)
        {
            PlaceAtStart(pieces);
            foreach (var p in pieces)
            {
                if (p.Placed) continue;
                var shape = ShapeOf(p.FurnitureId);
                if (shape == null || shape.Wall) continue;
                var floor = new List<Tile>(Grid.Floor);
                floor.Sort((x, y) => x.A != y.A ? x.A.CompareTo(y.A) : x.B.CompareTo(y.B));
                foreach (var t in floor)
                {
                    var placed = false;
                    foreach (var f in new[] { Facing.A, Facing.B })
                        if (Grid.CanPlace(shape, p.Level, t.A, t.B, f, p) == null)
                        {
                            p.Placed = true;
                            p.A = t.A;
                            p.B = t.B;
                            p.Facing = f;
                            _grid = null;
                            placed = true;
                            break;
                        }
                    if (placed) break;
                }
            }
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

        /// <summary>Puts away every placed piece that no longer fits (after the shell changed). Returns them.</summary>
        private List<Placement> Revalidate()
        {
            var stored = new List<Placement>();
            foreach (var p in State.Furniture)
            {
                if (!p.Placed) continue;
                if (Grid.CanPlace(ShapeOf(p.FurnitureId), p.Level, p.A, p.B, p.Facing, p) == null) continue;
                p.Placed = false;
                stored.Add(p);
                _grid = null;
            }
            return stored;
        }

        /// <summary>Puts these pieces where a new shop has the same kind of piece, if that spot is free.</summary>
        private void PlaceAtStart(List<Placement> pieces)
        {
            var used = new HashSet<ShopDefinition.StartingPiece>();
            foreach (var p in pieces)
                foreach (var s in Definition.StartingFurniture)
                {
                    if (used.Contains(s) || s.furniture == null || s.furniture.Id != p.FurnitureId) continue;
                    var f = s.facingB ? Facing.B : Facing.A;
                    if (Grid.CanPlace(ShapeOf(p.FurnitureId), p.Level, s.tile.x, s.tile.y, f, p) != null) continue;
                    used.Add(s);
                    p.Placed = true;
                    p.A = s.tile.x;
                    p.B = s.tile.y;
                    p.Facing = f;
                    _grid = null;
                    break;
                }
        }

        /// <summary>Saved shop layout version (see Read).</summary>
        public const int LayoutVersion = 2;

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
            var d = new ShopSaveData { NextId = State.NextId, Expansions = State.Expansions, Layout = LayoutVersion, Wagon = State.WagonLevel };
            foreach (var b in State.Blocks) d.Blocks.Add(new[] { b.A, b.B });
            foreach (var p in State.Furniture)
                d.Furniture.Add(new ShopFurnitureSaveData
                {
                    Id = p.Id, Furniture = p.FurnitureId, Level = p.Level, Placed = p.Placed, A = p.A, B = p.B, Facing = p.Facing.ToString(),
                    Shown = new List<string>(p.Shown),
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
                var placement = new Placement
                {
                    Id = f.Id, FurnitureId = f.Furniture, Level = Math.Max(1, Math.Min(f.Level, def.MaxLevel)), Placed = f.Placed,
                    A = f.A, B = f.B, Facing = f.Facing == "B" ? Facing.B : Facing.A,
                };
                if (f.Shown != null)
                    foreach (var id in f.Shown)   // items no longer in the content fall back to Auto
                        placement.Shown.Add(!string.IsNullOrEmpty(id) && _catalogue.TryGet<ItemDefinition>(id, out _) ? id : "");
                State.Furniture.Add(placement);
            }
            if (Wagon)
            {
                if (d.Wagon > 0) SetCarpet(d.Wagon);
                else MoveOntoTheWagon();
                Revalidate();
                if (d.Wagon == 0) AutoPlace(State.Furniture.FindAll(p => !p.Placed));   // moved onto the carpet: set it out
                Changed?.Invoke();
                return;
            }
            // Layout 2 (2026-10-15): the start grew from 4 x 6 to 6 x 6 and the entrance to 2 tiles. Older shops get the
            // new start blocks; pieces that no longer fit go back to where a new shop has them, else into storage.
            foreach (var b in _config.StartBlocks) if (!State.Blocks.Contains(b)) State.Blocks.Add(b);
            var stored = Revalidate();
            if (d.Layout < LayoutVersion) PlaceAtStart(stored);
            Changed?.Invoke();
        }

        /// <summary>A new shop: the start blocks and the starting furniture where the content places it.</summary>
        private void Fresh()
        {
            if (Wagon) SetCarpet(1);
            else foreach (var b in _config.StartBlocks) State.Blocks.Add(b);
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
