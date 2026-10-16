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
            /// <summary>
            /// The land's right-hand edge (screen right, the side the camp's group stands on) near this camp: cells to the
            /// right of the line (0: no edge pull). The camp looks over the drop to the castle (Kevin, 2026-10-16).
            /// </summary>
            public double EdgeRight;
            /// <summary>
            /// Where the camp's group stands (cells) and how far right of it on screen the drop comes in (world units; 0:
            /// none): a cove in the land's edge, its cliff falling away along the diagonals above and below the group.
            /// </summary>
            public double AX, AY, CoveGap;
            /// <summary>World units above the group the bay's cliff keeps running up and left (opening the top of the view).</summary>
            public double BayHeight;
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

        // ------------------------------------------------------------------ a level (Kevin, 2026-10-16: gated levels)

        /// <summary>
        /// A level: one square plateau of <see cref="LevelSize"/> cells from (<see cref="LevelX0"/>, <see cref="LevelY0"/>),
        /// its edges a little ragged, cliffs all round (the valley view beyond). The camp stands at its front (down the
        /// screen); the road runs from it straight across to the gate at the far corner (up the screen, toward the castle).
        /// Left of the road (screen left) is flat forest; right of it, the hills, the Mine's ground.
        /// </summary>
        public bool Plateau;
        public int LevelX0, LevelY0, LevelSize = 60;

        /// <summary>Which level this is (0-based): its band of wood and ore.</summary>
        public int Level;

        /// <summary>The cells the road ends short of the far corner.</summary>
        public const int PlateauRoadInset = 3;

        /// <summary>How ragged a level's edge is (cells either way).</summary>
        public double PlateauEdgeNoise = 1.2;

        private void BuildPlateau()
        {
            double ex = LevelX0 + LevelSize - 1 - PlateauRoadInset, ey = LevelY0 + LevelSize - 1 - PlateauRoadInset;
            var len = Math.Sqrt((ex - StartX) * (ex - StartX) + (ey - StartY) * (ey - StartY));
            var n = Math.Max(1, (int)Math.Ceiling(len));
            for (var k = 0; k <= n; k++)
                _line.Add((StartX + (ex - StartX) * k / n, StartY + (ey - StartY) * k / n, len * k / n));
        }

        private bool InPlateau(int x, int y)
        {
            var inside = Math.Min(Math.Min(x - LevelX0, LevelX0 + LevelSize - 1 - x), Math.Min(y - LevelY0, LevelY0 + LevelSize - 1 - y));
            return inside + PlateauEdgeNoise * (Noise2(x / 4.0, y / 4.0, 31) * 2 - 1) >= 0;
        }

        // ------------------------------------------------------------------ the switchback pass (Kevin, 2026-10-16)

        /// <summary>
        /// The road as a switchback pass (Astra's pass pack): straight legs alternating along +x (up-right) and +y (up-left),
        /// joined by quarter-circle turns of <see cref="TurnRadius"/> cells (the paintings' road radius). The land is the
        /// legs' fixed width either side of the line (the paintings' 16 tiles), plus the camps' clearings.
        /// </summary>
        public bool Switchback;
        public int Legs = 6;
        public double LegLength = 40, FirstLegLength = 40, TurnRadius = 16;

        /// <summary>The last leg runs on past its camp to the castle's gate.</summary>
        public double FinalStretch = 24;

        /// <summary>A leg of the pass: along x or y, from s0 to s1 along the line.</summary>
        public struct LegSpan { public double S0, S1; public bool AlongX; }

        /// <summary>A turn between legs: its circle's centre (cells), from s0 to s1 along the line; left turns go from +x to +y.</summary>
        public struct TurnSpan { public double S0, S1, CX, CY; public bool Left; public double Mid => (S0 + S1) * 0.5; }

        public readonly List<LegSpan> LegSpans = new List<LegSpan>();
        public readonly List<TurnSpan> TurnSpans = new List<TurnSpan>();

        /// <summary>The middle of leg <paramref name="i"/> (0-based) along the line.</summary>
        public double LegMid(int i)
        {
            Build();
            var l = LegSpans[Math.Max(0, Math.Min(i, LegSpans.Count - 1))];
            return (l.S0 + l.S1) * 0.5;
        }

        /// <summary>
        /// How far below the first leg each leg is drawn, world units (Astra's 512 px cliff): the pass looks lower leg by leg
        /// while the ground stays one flat plane to walk on (Kevin, 2026-10-16).
        /// </summary>
        public double DropStep = 2.0;

        /// <summary>
        /// How far down a distance along the line is drawn: <see cref="DropStep"/> per leg passed, easing through each turn as
        /// Astra's turns are painted (3t² - 2t³ across the turn). 0 off the pass.
        /// </summary>
        public double DropAt(double s)
        {
            if (!Switchback) return 0;
            Build();
            var d = 0.0;
            foreach (var t in TurnSpans)
            {
                // Eased across the turn and a little into the legs either side: on the inside of a turn the cells are packed
                // tight along the road, and a steeper drop would draw neighbouring cells over each other (the mapping must
                // stay one to one). The camps' ledges, mid-leg, stay flat.
                var from = t.S0 - DropRampIntoLegs;
                var to = t.S1 + DropRampIntoLegs;
                if (s >= to) d += DropStep;
                else if (s > from)
                {
                    var u = (s - from) / Math.Max(1e-6, to - from);
                    d += DropStep * u * u * (3 - 2 * u);
                }
            }
            return d;
        }

        /// <summary>Cells of each leg either side of a turn over which its drop also eases.</summary>
        public double DropRampIntoLegs = 7;

        /// <summary>How far down a cell is drawn (its nearest point of the line).</summary>
        public double DropAtCell(double cx, double cy) => Switchback ? DropAt(Locate((int)Math.Round(cx), (int)Math.Round(cy)).s) : 0;

        /// <summary>
        /// Where a cell (fractional cells allowed) stands in the world: the isometric grid (x right-up, y left-up; a cell's
        /// centre at ((x - y) / 2, (x + y) / 4 + 1/4)), lowered by its leg's drop.
        /// </summary>
        public (double x, double y) CellToWorld(double cx, double cy) => ((cx - cy) * 0.5, (cx + cy) * 0.25 + 0.25 - DropAtCell(cx, cy));

        /// <summary>
        /// The cell (fractional; floor it for the cell index) at a world point: the grid's inverse, undoing the drop of the
        /// leg the point is drawn on (found by a few rounds of refinement: the legs never overlap on screen).
        /// </summary>
        public (double cx, double cy) WorldToCell(double wx, double wy)
        {
            (double cx, double cy) Undrop(double drop) => (wx + 2 * (wy + drop) - 0.5, 2 * (wy + drop) - wx - 0.5);
            if (!Switchback) return Undrop(0);
            Build();
            // Which leg's level the point is drawn at: the one whose drop the cell found there agrees with...
            var d = 0.0;
            var best = double.MaxValue;
            for (var k = 0; k <= TurnSpans.Count; k++)
            {
                var c0 = Undrop(k * DropStep);
                var err = Math.Abs(DropAtCell(c0.cx, c0.cy) - k * DropStep);
                if (err < best) { best = err; d = k * DropStep; }
            }
            // ...then settled on the drop of the very cell (through a turn the drop changes from cell to cell).
            var c = Undrop(d);
            for (var i = 0; i < 12; i++)
            {
                var nd = DropAtCell(c.cx, c.cy);
                if (Math.Abs(nd - d) < 1e-5) break;
                d = nd;
                c = Undrop(d);
            }
            return c;
        }

        /// <summary>Which leg (0-based) a distance along the line belongs to: a turn belongs to the leg before its middle.</summary>
        public int LegIndexAt(double s)
        {
            Build();
            var i = 0;
            foreach (var t in TurnSpans) if (s >= t.Mid) i++;
            return i;
        }

        private void BuildSwitchback()
        {
            var raw = new List<(double x, double y)>();
            double x = StartX, y = StartY;
            var alongX = true;
            var legStarts = new List<int>();
            var legEnds = new List<int>();
            var turnStarts = new List<int>();
            var turnEnds = new List<int>();
            var centres = new List<(double x, double y, bool left)>();
            for (var leg = 0; leg < Legs; leg++)
            {
                var len = leg == 0 ? FirstLegLength : leg == Legs - 1 ? LegLength + FinalStretch : LegLength;
                legStarts.Add(raw.Count);
                var n = Math.Max(1, (int)Math.Ceiling(len * 2));
                for (var k = 0; k < n; k++) raw.Add(alongX ? (x + len * k / n, y) : (x, y + len * k / n));
                if (alongX) x += len; else y += len;
                legEnds.Add(raw.Count);
                if (leg == Legs - 1) break;
                // A quarter circle: left (from +x to +y) round a centre on the left of travel; right (from +y to +x) likewise.
                var left = alongX;
                var cx = left ? x : x + TurnRadius;
                var cy = left ? y + TurnRadius : y;
                centres.Add((cx, cy, left));
                turnStarts.Add(raw.Count);
                var m = Math.Max(8, (int)Math.Ceiling(Math.PI * TurnRadius));
                for (var k = 0; k < m; k++)
                {
                    var a = Math.PI * 0.5 * k / m;
                    raw.Add(left ? (cx + TurnRadius * Math.Sin(a), cy - TurnRadius * Math.Cos(a)) : (cx - TurnRadius * Math.Cos(a), cy + TurnRadius * Math.Sin(a)));
                }
                x = left ? cx + TurnRadius : cx;
                y = left ? cy : cy + TurnRadius;
                turnEnds.Add(raw.Count);
                alongX = !alongX;
            }
            raw.Add((x, y));
            var s = 0.0;
            for (var i = 0; i < raw.Count; i++)
            {
                if (i > 0) s += Dist(raw[i - 1], raw[i]);
                _line.Add((raw[i].x, raw[i].y, s));
            }
            double SAt(int i) => _line[Math.Min(i, _line.Count - 1)].s;
            LegSpans.Clear();
            for (var i = 0; i < legStarts.Count; i++) LegSpans.Add(new LegSpan { S0 = SAt(legStarts[i]), S1 = SAt(legEnds[i]), AlongX = i % 2 == 0 });
            TurnSpans.Clear();
            for (var i = 0; i < turnStarts.Count; i++)
                TurnSpans.Add(new TurnSpan { S0 = SAt(turnStarts[i]), S1 = SAt(turnEnds[i]), CX = centres[i].x, CY = centres[i].y, Left = centres[i].left });
        }

        private void Build()
        {
            if (_built) return;
            _built = true;
            if (Plateau)
            {
                BuildPlateau();
                return;
            }
            if (Switchback)
            {
                BuildSwitchback();
                return;
            }
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
                if (Plateau)
                {
                    var m = (int)Math.Ceiling(PlateauEdgeNoise) + 1;
                    return (LevelX0 - m, LevelY0 - m, LevelX0 + LevelSize - 1 + m, LevelY0 + LevelSize - 1 + m);
                }
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

        /// <summary>A point beside the centre line: <paramref name="off"/> cells to the left of travel (negative: to the right).</summary>
        public (double x, double y) PointBeside(double s, double off)
        {
            var (x, y) = PointAt(s);
            var (ax, ay) = PointAt(Math.Max(0, s - 1));
            var (bx, by) = PointAt(Math.Min(Length, s + 1));
            var tx = bx - ax;
            var ty = by - ay;
            var tl = Math.Sqrt(tx * tx + ty * ty);
            if (tl < 1e-9) return (x, y);
            tx /= tl;
            ty /= tl;
            return (x - ty * off, y + tx * off);
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
            if (Switchback) return w;   // the pass keeps the paintings' straight edges; a camp's clearing is its ledge
            foreach (var c in Clearings)
                w = Math.Max(w, Math.Max(c.RX, c.RY) + 10 - Math.Abs(s - c.S) * 0.4);   // a camp sits in a bay
            return w;
        }

        /// <summary>
        /// How strongly a camp's edge pull holds at <paramref name="s"/> (1 beside the camp, fading to 0 further along), and the
        /// camp's right-hand edge: near a camp the land ends a few cells past where its group stands, and the road swings
        /// out to that edge (Kevin, 2026-10-16: the camps look over the drop to the castle).
        /// </summary>
        public (double pull, double edge) EdgePull(double s)
        {
            Build();
            double best = 0, edge = 0;
            foreach (var c in Clearings)
            {
                if (c.EdgeRight <= 0) continue;
                var full = Math.Max(c.RX, c.RY) + 6;
                var d = Math.Abs(s - c.S);
                var p = d <= full ? 1 : d >= full + EdgeFade ? 0 : Smooth(1 - (d - full) / EdgeFade);
                if (p > best) { best = p; edge = c.EdgeRight; }
            }
            return (best, edge);
        }

        /// <summary>
        /// The drop right of a camp's group (Kevin, 2026-10-16: each camp looks over the drop to the castle), even through
        /// its clearing: on screen, CoveGap world units right of the group at its level, the cliff falling away along the
        /// diagonals above and below (so what stands below and right of the group, camp 1's Trader and Workshop, keeps
        /// its ground).
        /// </summary>
        public bool InCove(int x, int y)
        {
            Build();
            foreach (var c in Clearings)
            {
                if (c.CoveGap <= 0) continue;
                var dx = ((x - y) - (c.AX - c.AY)) * 0.5;      // world units across the screen
                var dy = ((x + y) - (c.AX + c.AY)) * 0.25;     // and up it
                var wobble = 0.4 * (Noise2(x / 3.0, y / 3.0, 61) * 2 - 1);
                if (dx > BayEdge(c, dy) + wobble) return true;
            }
            return false;
        }

        /// <summary>Flat world units above the group before the bay's cliff turns up and left (room for the shop).</summary>
        public const double BayFlat = 2;

        /// <summary>
        /// Where the bay's cliff is across the screen (world units right of the group) at a height above it: below the group
        /// it falls away down and right (what stands there keeps its ground); beside it, CoveGap; above it, it runs up and
        /// left for BayHeight, opening the top of the view (the castle and the valley, as in Kevin's screenshot), then turns
        /// back up and right to the land's edge.
        /// </summary>
        public static double BayEdge(Clearing c, double dy)
        {
            if (dy <= 0) return c.CoveGap - 2 * dy;
            if (dy <= BayFlat) return c.CoveGap;
            var top = BayFlat + c.BayHeight;
            if (dy <= top) return c.CoveGap - (dy - BayFlat);
            return c.CoveGap - c.BayHeight + 2 * (dy - top);
        }

        /// <summary>Cells along the road over which a camp's edge pull fades out.</summary>
        public double EdgeFade = 24;

        private static double Smooth(double t) => t * t * (3 - 2 * t);

        /// <summary>How far the land reaches on a side: to the left of travel (<paramref name="right"/> false), or to the right.</summary>
        public double SideWidthAt(double s, bool right)
        {
            var w = WidthAt(s);
            if (!right) return w;
            var (p, edge) = EdgePull(s);
            return p <= 0 ? w : w + (edge - w) * p;
        }

        public bool InLand(int x, int y)
        {
            if (Plateau) return InPlateau(x, y) || InClearing(x, y);
            var (s, off, d) = Locate(x, y);
            var noise = EdgeNoise * (Noise2(x / 5.0, y / 5.0, 31) * 2 - 1);
            if (InCove(x, y)) return false;
            if (off < 0) noise *= 1 - 0.7 * EdgePull(s).pull;   // the edge is less ragged beside a camp
            if (InClearing(x, y)) return true;
            return d < SideWidthAt(s, off < 0) + noise;
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
            return Math.Abs(off - RoadOffsetAt(s));
        }

        /// <summary>The road's offset from the centre line (left of travel): it wanders, and near a camp swings out along the edge.</summary>
        public double RoadOffsetAt(double s)
        {
            var key = (int)Math.Round(s * 4);
            if (_roadOffsets.TryGetValue(key, out var known)) return known;
            var wander = RoadWobble * (Noise1(s / 18.0, 53) * 2 - 1);
            var (p, edge) = EdgePull(s);
            var off = p <= 0 ? wander : wander + (-(edge - RoadEdgeInset) - wander) * p;
            // Never through a camp's bay: the road keeps a few cells of land between it and the drop.
            for (var guard = 0; guard < 40 && NearCove(s, off); guard++) off += 1;
            _roadOffsets[key] = off;
            return off;
        }

        private readonly Dictionary<int, double> _roadOffsets = new Dictionary<int, double>();

        private bool NearCove(double s, double off)
        {
            for (var k = 0; k <= (int)RoadEdgeInset; k++)
            {
                var (x, y) = PointBeside(s, off - k);
                if (InCove((int)Math.Round(x), (int)Math.Round(y))) return true;
            }
            return false;
        }

        /// <summary>Cells between the road and the land's edge where it runs along the drop near a camp.</summary>
        public double RoadEdgeInset = 4;

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
        public int BandAt(int x, int y) => Plateau ? 1 + Level : Switchback ? 1 + LegIndexAt(Locate(x, y).s)
            : 1 + (int)Math.Max(0, Math.Floor((Locate(x, y).s - CampS) / Math.Max(1, BandLength)));

        /// <summary>
        /// Extra effort within a band: three steps (0, 1, 2) across its length, so a band's last trees stay easier than
        /// the next band's first (step 7: bands are 80 long; at one step per 15 cells they overtook the next band).
        /// </summary>
        public int ContinuationAt(int x, int y)
        {
            if (Plateau)
            {
                // Three steps from the camp to the gate.
                var s = Locate(x, y).s;
                return Math.Max(0, Math.Min(2, (int)((s - CampS) / Math.Max(1, Length - CampS) * 3)));
            }
            if (Switchback)
            {
                // On the pass a band is a leg (from the turn before it to the turn after): three steps across it.
                var s = Locate(x, y).s;
                var leg = LegIndexAt(s);
                var from = leg == 0 ? CampS : TurnSpans[leg - 1].Mid;
                var to = leg < TurnSpans.Count ? TurnSpans[leg].Mid : Length;
                return Math.Max(0, Math.Min(2, (int)((s - from) / Math.Max(1, to - from) * 3)));
            }
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
            if (Plateau && Locate(x, y).off > 0) return 0;   // a level's forest side is flat
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
