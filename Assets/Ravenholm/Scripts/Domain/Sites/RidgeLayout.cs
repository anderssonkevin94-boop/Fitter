using System;
using System.Collections.Generic;

namespace Ravenholm.Domain.Sites
{
    /// <summary>
    /// The land of the journey (Kevin, 2026-10-15): a long, wide, winding mountain top running forward (up the screen)
    /// toward the castle. Its centre line alternates up-right (+x) and up-left (+y) stretches with a wobble, its width
    /// breathes, and its edges are ragged. An old cobble road wanders along it, mostly overgrown. Camps are clearings on
    /// it; gates are bands across its whole width that need a better tool. Everything further forward is tougher (bands
    /// along the road). Cells are map cells (x right-up, y left-up). Engine-free and deterministic: the shape is authored
    /// (stretches, widths), the details come from <see cref="Seed"/>.
    /// </summary>
    public sealed class RidgeLayout
    {
        public struct Stretch
        {
            public bool AlongX;   // up-right; else up-left
            public int Length;    // cells
        }

        /// <summary>A clearing: an ellipse round a point of the line (or round an authored centre), where the ridge widens into a bay.</summary>
        public sealed class Clearing
        {
            public double S;
            public bool HasCentre;   // authored centre (camp 1's, round its town) rather than the line's point at S
            public double CX, CY, RX, RY;
        }

        /// <summary>A band across the whole ridge that needs a better tool (old trees: the axe; a rock wall: the pickaxe).</summary>
        public sealed class Gate
        {
            public double S;
            public int Thickness = 4;
            public int Tier = 2;
            public bool Rock;
            /// <summary>A build project (a bridge, a causeway): the land is a gap until it is built; no tool needed.</summary>
            public bool Project;
        }

        public double StartX, StartY;
        public readonly List<Stretch> Stretches = new List<Stretch>();
        public readonly List<Clearing> Clearings = new List<Clearing>();
        public readonly List<Gate> Gates = new List<Gate>();
        public double HalfWidth = 20, WidthWobble = 5, EdgeNoise = 3, LineWobble = 4, RoadWobble = 4, RoadHalfWidth = 1.3;
        public int BandLength = 45;
        public int FlatMargin = 3;
        public double CampS;   // bands count forward from here (camp 1)
        public int Seed = 1;

        private readonly List<(double x, double y, double s)> _line = new List<(double, double, double)>();
        private readonly Dictionary<long, (double s, double off, double d)> _located = new Dictionary<long, (double, double, double)>();
        private bool _built;

        public double Length { get { Build(); return _line.Count > 0 ? _line[_line.Count - 1].s : 0; } }

        // ------------------------------------------------------------------ the centre line

        private void Build()
        {
            if (_built) return;
            _built = true;
            // Corners of the authored stretches, smoothed (Chaikin) so the turns are bends, then sampled every cell.
            var corners = new List<(double x, double y)> { (StartX, StartY) };
            double cx = StartX, cy = StartY;
            foreach (var st in Stretches)
            {
                if (st.AlongX) cx += st.Length; else cy += st.Length;
                corners.Add((cx, cy));
            }
            for (var pass = 0; pass < 3 && corners.Count > 2; pass++)
            {
                var next = new List<(double, double)> { corners[0] };
                for (var i = 0; i + 1 < corners.Count; i++)
                {
                    var (ax, ay) = corners[i];
                    var (bx, by) = corners[i + 1];
                    next.Add((ax * 0.75 + bx * 0.25, ay * 0.75 + by * 0.25));
                    next.Add((ax * 0.25 + bx * 0.75, ay * 0.25 + by * 0.75));
                }
                next.Add(corners[corners.Count - 1]);
                corners = next;
            }
            var raw = new List<(double x, double y)>();
            for (var i = 0; i + 1 < corners.Count; i++)
            {
                var (ax, ay) = corners[i];
                var (bx, by) = corners[i + 1];
                var n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay))));
                for (var k = 0; k < n; k++) raw.Add((ax + (bx - ax) * k / n, ay + (by - ay) * k / n));
            }
            raw.Add(corners[corners.Count - 1]);
            // A slow sideways wobble, so even a straight stretch meanders.
            var s = 0.0;
            for (var i = 0; i < raw.Count; i++)
            {
                if (i > 0) s += Dist(raw[i - 1], raw[i]);
                var (tx, ty) = Tangent(raw, i);
                var w = LineWobble * (Noise1(s / 22.0, 11) * 2 - 1);
                _line.Add((raw[i].x - ty * w, raw[i].y + tx * w, 0));
            }
            s = 0;
            for (var i = 0; i < _line.Count; i++)
            {
                if (i > 0) s += Dist((_line[i - 1].x, _line[i - 1].y), (_line[i].x, _line[i].y));
                _line[i] = (_line[i].x, _line[i].y, s);
            }
        }

        /// <summary>Cells that may be land: x from MinX to MaxX inclusive, likewise y.</summary>
        public (int minX, int minY, int maxX, int maxY) Extent
        {
            get
            {
                Build();
                var pad = HalfWidth + WidthWobble + EdgeNoise + 4;
                foreach (var c in Clearings) pad = Math.Max(pad, Math.Max(c.RX, c.RY) + 14);
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (var p in _line)
                {
                    minX = Math.Min(minX, (int)Math.Floor(p.x - pad));
                    minY = Math.Min(minY, (int)Math.Floor(p.y - pad));
                    maxX = Math.Max(maxX, (int)Math.Ceiling(p.x + pad));
                    maxY = Math.Max(maxY, (int)Math.Ceiling(p.y + pad));
                }
                foreach (var c in Clearings)
                {
                    var (cx, cy) = CentreOf(c);
                    minX = Math.Min(minX, (int)Math.Floor(cx - c.RX - 4));
                    minY = Math.Min(minY, (int)Math.Floor(cy - c.RY - 4));
                    maxX = Math.Max(maxX, (int)Math.Ceiling(cx + c.RX + 4));
                    maxY = Math.Max(maxY, (int)Math.Ceiling(cy + c.RY + 4));
                }
                return (minX, minY, maxX, maxY);
            }
        }

        private static double Dist((double x, double y) a, (double x, double y) b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

        private static (double x, double y) Tangent(List<(double x, double y)> pts, int i)
        {
            var a = pts[Math.Max(0, i - 1)];
            var b = pts[Math.Min(pts.Count - 1, i + 1)];
            var dx = b.x - a.x;
            var dy = b.y - a.y;
            var l = Math.Sqrt(dx * dx + dy * dy);
            return l < 1e-9 ? (1, 0) : (dx / l, dy / l);
        }

        /// <summary>A point of the centre line at distance <paramref name="s"/> along it.</summary>
        public (double x, double y) PointAt(double s)
        {
            Build();
            if (_line.Count == 0) return (StartX, StartY);
            var i = 0;
            while (i + 1 < _line.Count && _line[i + 1].s < s) i++;
            if (i + 1 >= _line.Count) return (_line[i].x, _line[i].y);
            var a = _line[i];
            var b = _line[i + 1];
            var k = b.s > a.s ? Math.Max(0, Math.Min(1, (s - a.s) / (b.s - a.s))) : 0;
            return (a.x + (b.x - a.x) * k, a.y + (b.y - a.y) * k);
        }

        /// <summary>
        /// Where a cell lies against the centre line: how far along (<c>s</c>), its signed sideways offset (positive to
        /// the left of travel), and its distance from the line (rounded at both ends).
        /// </summary>
        public (double s, double off, double d) Locate(int x, int y)
        {
            Build();
            var key = ((long)x << 32) ^ (uint)y;
            if (_located.TryGetValue(key, out var hit)) return hit;
            var best = double.MaxValue;
            var bi = 0;
            for (var i = 0; i < _line.Count; i++)
            {
                var dx = x - _line[i].x;
                var dy = y - _line[i].y;
                var d2 = dx * dx + dy * dy;
                if (d2 < best) { best = d2; bi = i; }
            }
            var p = _line[bi];
            var a = _line[Math.Max(0, bi - 1)];
            var b = _line[Math.Min(_line.Count - 1, bi + 1)];
            var tx = b.x - a.x;
            var ty = b.y - a.y;
            var tl = Math.Sqrt(tx * tx + ty * ty);
            if (tl > 1e-9) { tx /= tl; ty /= tl; }
            var off = tx * (y - p.y) - ty * (x - p.x);
            var r = (p.s, off, Math.Sqrt(best));
            _located[key] = r;
            return r;
        }

        // ------------------------------------------------------------------ the land

        public double WidthAt(double s)
        {
            var w = HalfWidth + WidthWobble * (Noise1(s / 30.0, 23) * 2 - 1);
            foreach (var c in Clearings)
                w = Math.Max(w, Math.Max(c.RX, c.RY) + 10 - Math.Abs(s - c.S) * 0.4);   // a camp sits in a bay
            return w;
        }

        public bool InLand(int x, int y)
        {
            if (InClearing(x, y)) return true;
            var (s, _, d) = Locate(x, y);
            return d < WidthAt(s) + EdgeNoise * (Noise2(x / 5.0, y / 5.0, 31) * 2 - 1);
        }

        private (double x, double y) CentreOf(Clearing c) => c.HasCentre ? (c.CX, c.CY) : PointAt(c.S);

        /// <summary>How far inside a clearing a cell is: below 1 inside (ragged edge), the distance in cells past its edge otherwise.</summary>
        private double ClearingMetric(Clearing c, int x, int y)
        {
            var (cx, cy) = CentreOf(c);
            var ex = (x - cx) / c.RX;
            var ey = (y - cy) / c.RY;
            return Math.Sqrt(ex * ex + ey * ey) + 0.12 * (Noise2(x / 4.0, y / 4.0, 41) * 2 - 1);
        }

        public bool InClearing(int x, int y)
        {
            Build();
            foreach (var c in Clearings) if (ClearingMetric(c, x, y) < 1) return true;
            return false;
        }

        /// <summary>Cells from the nearest clearing's edge (0 inside one).</summary>
        public double ClearingDistance(int x, int y)
        {
            Build();
            var best = double.MaxValue;
            foreach (var c in Clearings) best = Math.Min(best, Math.Max(0, ClearingMetric(c, x, y) - 1) * Math.Min(c.RX, c.RY));
            return best;
        }

        public bool InGrove(int x, int y) => InLand(x, y) && !InClearing(x, y) && !(GateAt(x, y) is Gate g && g.Project);

        /// <summary>A cell of a build project's gap (a ravine to bridge, a flood to cross): no ground until it is built.</summary>
        public bool InProjectGap(int x, int y) => InLand(x, y) && GateAt(x, y) is Gate g && g.Project;

        /// <summary>The index of the gate a cell lies in (-1: none).</summary>
        public int GateIndexAt(int x, int y)
        {
            var s = Locate(x, y).s;
            for (var i = 0; i < Gates.Count; i++) if (Math.Abs(s - Gates[i].S) < Gates[i].Thickness * 0.5) return i;
            return -1;
        }

        /// <summary>On the old road (it wanders inside the ridge; clearings have their own paths).</summary>
        public bool OnRoad(int x, int y) => RoadDistance(x, y) < RoadHalfWidth && InLand(x, y) && !InClearing(x, y);

        private double RoadDistance(int x, int y)
        {
            var (s, off, _) = Locate(x, y);
            return Math.Abs(off - RoadWobble * (Noise1(s / 18.0, 53) * 2 - 1));
        }

        /// <summary>The tool tier a gate needs here (0 = none).</summary>
        public int GateTierAt(int x, int y)
        {
            var s = Locate(x, y).s;
            foreach (var g in Gates) if (Math.Abs(s - g.S) < g.Thickness * 0.5) return g.Project ? 0 : g.Tier;
            return 0;
        }

        public Gate GateAt(int x, int y)
        {
            var s = Locate(x, y).s;
            foreach (var g in Gates) if (Math.Abs(s - g.S) < g.Thickness * 0.5) return g;
            return null;
        }

        /// <summary>Forward of camp 1, every <see cref="BandLength"/> cells is the next band (new wood, the Mine's next layer).</summary>
        public int BandAt(int x, int y) => 1 + (int)Math.Max(0, Math.Floor((Locate(x, y).s - CampS) / Math.Max(1, BandLength)));

        /// <summary>
        /// Extra effort within a band: three steps (0, 1, 2) across its length, so a band's last trees stay easier than
        /// the next band's first (step 7: bands are 80 long; at one step per 15 cells they overtook the next band).
        /// </summary>
        public int ContinuationAt(int x, int y)
        {
            var along = Math.Max(0, Locate(x, y).s - CampS);
            var band = Math.Max(1, BandLength);
            return (int)(along % band * 3 / band);
        }

        /// <summary>
        /// Ground the layout decides itself (the forest-mountains terrain decides the rest, see
        /// <see cref="ForestGrove.TerrainHeightAt"/>): off the land and at gates. A rock gate is a wall four blocks high;
        /// a tree gate is flat.
        /// </summary>
        public bool FixedHeight(int x, int y, out int height)
        {
            height = 0;
            if (!InGrove(x, y)) return true;
            var gate = GateAt(x, y);
            if (gate == null) return false;
            height = gate.Rock ? 4 : 0;
            return true;
        }

        /// <summary>
        /// The highest the hills may stand here: flat beside the clearings (<see cref="FlatMargin"/>) and along the road,
        /// rising two blocks a cell away from them.
        /// </summary>
        public int HeightCap(int x, int y)
        {
            var fromClearing = ClearingDistance(x, y) - FlatMargin;
            var fromRoad = RoadDistance(x, y) - RoadHalfWidth - 1;
            return Math.Max(0, (int)Math.Floor(Math.Min(fromClearing, fromRoad) * 2));
        }

        /// <summary>
        /// Whether a tree grows here, where the layout decides it: gates are solid; the old road is mostly open (a young
        /// tree here and there). False: the forest-mountains rule decides (thick in the valleys, thin up the hills).
        /// </summary>
        public bool FixedOpen(ulong seed, int x, int y, out bool open)
        {
            open = false;
            if (GateAt(x, y) != null) return true;
            if (!OnRoad(x, y)) return false;
            open = Hash01(x, y, 71 + (int)(seed % 997)) < 0.75;
            return true;
        }

        // ------------------------------------------------------------------ noise (deterministic, engine-free)

        private double Noise1(double t, int salt)
        {
            var i = (int)Math.Floor(t);
            var f = t - i;
            f = f * f * (3 - 2 * f);
            return Hash01(i, salt, Seed) * (1 - f) + Hash01(i + 1, salt, Seed) * f;
        }

        private double Noise2(double x, double y, int salt)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            var a = Hash01(x0, y0, salt + Seed * 7);
            var b = Hash01(x0 + 1, y0, salt + Seed * 7);
            var c = Hash01(x0, y0 + 1, salt + Seed * 7);
            var d = Hash01(x0 + 1, y0 + 1, salt + Seed * 7);
            return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
        }

        private static double Hash01(int x, int y, int salt)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + salt * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }
    }
}
