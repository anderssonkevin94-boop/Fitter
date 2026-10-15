using System;
using System.Collections.Generic;

namespace Ravenholm.Domain.Shop
{
    /// <summary>A shop floor tile: <see cref="A"/> runs from the front corner toward the right, <see cref="B"/> toward the left.</summary>
    public readonly struct Tile : IEquatable<Tile>
    {
        public readonly int A, B;
        public Tile(int a, int b) { A = a; B = b; }
        public bool Equals(Tile o) => A == o.A && B == o.B;
        public override bool Equals(object o) => o is Tile t && Equals(t);
        public override int GetHashCode() => A * 7919 + B;
        public override string ToString() => $"({A},{B})";
    }

    /// <summary>
    /// A tile's four edges. Back-left faces +B, back-right +A (away from the camera: full walls stand there);
    /// front-left faces -A, front-right -B (toward the camera: low walls).
    /// </summary>
    public enum EdgeSide { BackLeft, BackRight, FrontLeft, FrontRight }

    public enum EdgeKind { Tall, Low, Entrance }

    /// <summary>
    /// Floor pieces: A as drawn, B turned 90 degrees (footprint swapped). Wall pieces: A hangs on a back-left wall,
    /// B on a back-right wall.
    /// </summary>
    public enum Facing { A, B }

    public readonly struct Edge
    {
        public readonly Tile Tile;
        public readonly EdgeSide Side;
        public readonly EdgeKind Kind;
        public Edge(Tile t, EdgeSide s, EdgeKind k) { Tile = t; Side = s; Kind = k; }
    }

    /// <summary>A joint where wall pieces meet, in vertex coordinates (tile corners).</summary>
    public readonly struct Post
    {
        public readonly int A, B;
        public readonly bool Tall;
        public Post(int a, int b, bool tall) { A = a; B = b; Tall = tall; }
    }

    /// <summary>Shape of a furniture type: floor footprint per level (tiles along A x B), or wall edges.</summary>
    public sealed class FurnitureShape
    {
        public string Id;
        public bool Wall;
        public int WallEdges = 2;
        public readonly List<(int a, int b)> Footprints = new List<(int a, int b)>();
        public int MaxLevel => Math.Max(1, Footprints.Count);

        public (int a, int b) Footprint(int level, Facing f)
        {
            (int a, int b) fp = Footprints.Count == 0 ? (1, 1) : Footprints[Math.Max(0, Math.Min(level, Footprints.Count) - 1)];
            return f == Facing.A ? fp : (fp.b, fp.a);
        }
    }

    /// <summary>A piece the player owns: placed on the floor or a wall, or put away.</summary>
    public sealed class Placement
    {
        public string Id = "";
        public string FurnitureId = "";
        public int Level = 1;
        public bool Placed;
        public int A, B;
        public Facing Facing;
    }

    public sealed class ShopState
    {
        /// <summary>Owned 2 x 2 blocks (block coordinates: tiles 2i..2i+1, 2j..2j+1).</summary>
        public readonly List<Tile> Blocks = new List<Tile>();
        public readonly List<Placement> Furniture = new List<Placement>();
        public long NextId = 1;
        public int Expansions;
    }

    public sealed class ShopLayoutConfig
    {
        /// <summary>The lot, in blocks: the shop can grow to <see cref="LotA"/> x <see cref="LotB"/> blocks.</summary>
        public int LotA = 5, LotB = 5;
        public readonly List<Tile> StartBlocks = new List<Tile>();
        /// <summary>The tile just inside the entrance, and the front edge the entrance is cut into.</summary>
        public Tile Entrance;
        public EdgeSide EntranceSide = EdgeSide.FrontRight;
        /// <summary>A full wall covers floor this many depth steps behind it (2.2 units at 0.25 units a step).</summary>
        public int WallDepthSteps = 9;
    }

    /// <summary>
    /// The shop's tile grid (engine-free): which tiles are floor, where full walls, low walls and posts go, and whether a
    /// piece of furniture fits. Built from the owned blocks; one instance per state change is cheap enough.
    /// </summary>
    public sealed class ShopGrid
    {
        public const int BlockSize = 2;

        private readonly ShopState _s;
        private readonly ShopLayoutConfig _config;
        private readonly Func<string, FurnitureShape> _shapes;
        private readonly HashSet<Tile> _floor = new HashSet<Tile>();
        private readonly Dictionary<(Tile, EdgeSide), EdgeKind> _edges = new Dictionary<(Tile, EdgeSide), EdgeKind>();

        public ShopGrid(ShopState state, ShopLayoutConfig config, Func<string, FurnitureShape> shapes)
        {
            _s = state;
            _config = config;
            _shapes = shapes;
            foreach (var b in state.Blocks)
                for (var i = 0; i < BlockSize; i++)
                    for (var j = 0; j < BlockSize; j++)
                        _floor.Add(new Tile(b.A * BlockSize + i, b.B * BlockSize + j));
            BuildEdges();
        }

        public IEnumerable<Tile> Floor => _floor;
        public bool IsFloor(Tile t) => _floor.Contains(t);
        public bool IsFloor(int a, int b) => _floor.Contains(new Tile(a, b));
        public Tile Entrance => _config.Entrance;
        public ShopLayoutConfig Config => _config;

        public static Tile Neighbour(Tile t, EdgeSide s) => s switch
        {
            EdgeSide.BackLeft => new Tile(t.A, t.B + 1),
            EdgeSide.BackRight => new Tile(t.A + 1, t.B),
            EdgeSide.FrontLeft => new Tile(t.A - 1, t.B),
            _ => new Tile(t.A, t.B - 1),
        };

        // ------------------------------------------------------------------ shell

        public IEnumerable<Edge> Edges
        {
            get { foreach (var kv in _edges) yield return new Edge(kv.Key.Item1, kv.Key.Item2, kv.Value); }
        }

        public bool TryEdge(Tile t, EdgeSide s, out EdgeKind kind) => _edges.TryGetValue((t, s), out kind);

        private void BuildEdges()
        {
            foreach (var t in _floor)
                foreach (EdgeSide side in Enum.GetValues(typeof(EdgeSide)))
                {
                    if (_floor.Contains(Neighbour(t, side))) continue;
                    EdgeKind kind;
                    if (t.Equals(_config.Entrance) && side == _config.EntranceSide) kind = EdgeKind.Entrance;
                    else if (side == EdgeSide.BackLeft || side == EdgeSide.BackRight) kind = HidesFloor(t, side) ? EdgeKind.Low : EdgeKind.Tall;
                    else kind = EdgeKind.Low;
                    _edges[(t, side)] = kind;
                }
        }

        /// <summary>Screen span of an edge along x = A - B, and its depth (A + B) at its middle.</summary>
        private static (float lo, float hi, float depth) Span(Tile t, EdgeSide side)
        {
            float x = t.A - t.B, d = t.A + t.B;
            return side switch
            {
                EdgeSide.BackLeft => (x - 1, x, d + 1.5f),
                EdgeSide.BackRight => (x, x + 1, d + 1.5f),
                EdgeSide.FrontLeft => (x - 1, x, d + 0.5f),
                _ => (x, x + 1, d + 0.5f),
            };
        }

        /// <summary>Whether a full wall on this edge would stand in front of floor further back (an inside corner).</summary>
        private bool HidesFloor(Tile t, EdgeSide side)
        {
            var (lo, hi, depth) = Span(t, side);
            foreach (var f in _floor)
            {
                float fx = f.A - f.B, fd = f.A + f.B + 1;
                if (fd <= depth || fd - depth > _config.WallDepthSteps) continue;
                if (Math.Max(lo, fx - 1) < Math.Min(hi, fx + 1) - 0.001f) return true;
            }
            return false;
        }

        /// <summary>Joints: wherever a run of wall pieces turns, ends, or changes between full and low.</summary>
        public List<Post> Posts()
        {
            var at = new Dictionary<(int, int), List<(EdgeSide side, EdgeKind kind)>>();
            void Add(int a, int b, EdgeSide s, EdgeKind k)
            {
                if (!at.TryGetValue((a, b), out var l)) at[(a, b)] = l = new List<(EdgeSide, EdgeKind)>();
                l.Add((s, k));
            }
            foreach (var kv in _edges)
            {
                var (t, s) = kv.Key;
                var ((a0, b0), (a1, b1)) = Ends(t, s);
                Add(a0, b0, s, kv.Value);
                Add(a1, b1, s, kv.Value);
            }
            var posts = new List<Post>();
            foreach (var kv in at)
            {
                var l = kv.Value;
                var straight = l.Count == 2 && Axis(l[0].side) == Axis(l[1].side) && l[0].kind == l[1].kind;
                if (straight) continue;
                posts.Add(new Post(kv.Key.Item1, kv.Key.Item2, l.Exists(x => x.kind == EdgeKind.Tall)));
            }
            posts.Sort((x, y) => (x.A + x.B).CompareTo(y.A + y.B));
            return posts;
        }

        private static bool Axis(EdgeSide s) => s == EdgeSide.BackLeft || s == EdgeSide.FrontRight;   // edges running along A

        /// <summary>The edge's two corners in vertex coordinates (vertex (a, b) is the front corner of tile (a, b)).</summary>
        public static ((int, int), (int, int)) Ends(Tile t, EdgeSide s) => s switch
        {
            EdgeSide.BackLeft => ((t.A, t.B + 1), (t.A + 1, t.B + 1)),
            EdgeSide.BackRight => ((t.A + 1, t.B), (t.A + 1, t.B + 1)),
            EdgeSide.FrontLeft => ((t.A, t.B), (t.A, t.B + 1)),
            _ => ((t.A, t.B), (t.A + 1, t.B)),
        };

        // ------------------------------------------------------------------ furniture

        public IEnumerable<Tile> FootprintTiles(FurnitureShape shape, int level, int a, int b, Facing f)
        {
            var (fa, fb) = shape.Footprint(level, f);
            for (var i = 0; i < fa; i++)
                for (var j = 0; j < fb; j++)
                    yield return new Tile(a + i, b + j);
        }

        /// <summary>The wall edges a wall piece hangs on: back-left edges along A (facing A) or back-right edges along B.</summary>
        public IEnumerable<(Tile tile, EdgeSide side)> WallEdges(FurnitureShape shape, int a, int b, Facing f)
        {
            for (var i = 0; i < shape.WallEdges; i++)
                yield return f == Facing.A ? (new Tile(a + i, b), EdgeSide.BackLeft) : (new Tile(a, b + i), EdgeSide.BackRight);
        }

        /// <summary>Why the piece cannot go there (null = it can). <paramref name="ignore"/> is the piece being moved.</summary>
        public string CanPlace(FurnitureShape shape, int level, int a, int b, Facing f, Placement ignore = null)
        {
            if (shape == null) return "Unknown furniture";
            if (shape.Wall)
            {
                var mine = new HashSet<(Tile, EdgeSide)>();
                foreach (var e in WallEdges(shape, a, b, f))
                {
                    if (!_edges.TryGetValue(e, out var k) || k != EdgeKind.Tall) return "Needs a full wall";
                    mine.Add(e);
                }
                foreach (var p in _s.Furniture)
                {
                    if (!p.Placed || p == ignore) continue;
                    var other = _shapes(p.FurnitureId);
                    if (other == null || !other.Wall) continue;
                    foreach (var e in WallEdges(other, p.A, p.B, p.Facing))
                        if (mine.Contains(e)) return "Something already hangs there";
                }
                return null;
            }

            var occupied = Occupied(ignore);
            var tiles = new List<Tile>(FootprintTiles(shape, level, a, b, f));
            foreach (var t in tiles)
            {
                if (!_floor.Contains(t)) return "Not on the shop floor";
                if (occupied.Contains(t)) return "Something is in the way";
                if (t.Equals(_config.Entrance)) return "Keep the entrance clear";
            }
            foreach (var t in tiles) occupied.Add(t);
            return Unreachable(occupied);
        }

        /// <summary>Floor tiles covered by placed floor furniture.</summary>
        public HashSet<Tile> Occupied(Placement ignore = null)
        {
            var occupied = new HashSet<Tile>();
            foreach (var p in _s.Furniture)
            {
                if (!p.Placed || p == ignore) continue;
                var shape = _shapes(p.FurnitureId);
                if (shape == null || shape.Wall) continue;
                foreach (var t in FootprintTiles(shape, p.Level, p.A, p.B, p.Facing)) occupied.Add(t);
            }
            return occupied;
        }

        /// <summary>Every floor piece must border a free tile that can be walked to from the entrance.</summary>
        private string Unreachable(HashSet<Tile> occupied)
        {
            var reach = new HashSet<Tile>();
            var queue = new Queue<Tile>();
            if (_floor.Contains(_config.Entrance) && !occupied.Contains(_config.Entrance)) { reach.Add(_config.Entrance); queue.Enqueue(_config.Entrance); }
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                foreach (EdgeSide s in Enum.GetValues(typeof(EdgeSide)))
                {
                    var n = Neighbour(t, s);
                    if (_floor.Contains(n) && !occupied.Contains(n) && reach.Add(n)) queue.Enqueue(n);
                }
            }
            foreach (var t in occupied)
            {
                var ok = false;
                foreach (EdgeSide s in Enum.GetValues(typeof(EdgeSide)))
                    if (reach.Contains(Neighbour(t, s))) { ok = true; break; }
                if (!ok) return "That would block the way to something";
            }
            return null;
        }

        // ------------------------------------------------------------------ expansion

        public bool Owns(Tile block) => _s.Blocks.Contains(block);

        public IEnumerable<Tile> BuyableBlocks()
        {
            for (var i = 0; i < _config.LotA; i++)
                for (var j = 0; j < _config.LotB; j++)
                    if (CanBuy(new Tile(i, j)) == null) yield return new Tile(i, j);
        }

        public string CanBuy(Tile block)
        {
            if (block.A < 0 || block.B < 0 || block.A >= _config.LotA || block.B >= _config.LotB) return "Outside the lot";
            if (_s.Blocks.Contains(block)) return "Already yours";
            foreach (EdgeSide s in Enum.GetValues(typeof(EdgeSide)))
                if (_s.Blocks.Contains(Neighbour(block, s))) return null;
            return "Must join the shop";
        }
    }
}
