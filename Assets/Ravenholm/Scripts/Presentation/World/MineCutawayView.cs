using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using TMPro;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The Mine as an angled cutaway (GDD 6 LOCKED: "angled cutaway, visibly stacked geological layers"): a block
    /// set into the town's front edge whose two visible faces show the strata, with a straight shaft cut into it.
    ///
    /// The block is a fixed-size window into the earth: its footprint and face height never change, however deep
    /// the company digs. Depth scrolls inside it. The working layer and the unknown layer below always sit in the
    /// same place; finished layers compress into thin bands at the top lip (older ones merge into one labelled
    /// band); the shaft keeps its full width; its floor drops as the current section is worked; "Continue here"
    /// adds a side tunnel stub. Purely presentation: everything is read from the Mine's simulation record.
    /// </summary>
    public sealed class MineCutawayView : MonoBehaviour
    {
        public const string SiteId = "site.mine";
        public const int Footprint = 4;          // cells per side (iso tiles)
        public const float Inset = 0.7f;         // rim width around the shaft, in cells
        public const float FaceHeight = 2.6f;    // visible face height (world units), constant at any depth
        private const float HistoryBand = 0.1f;
        private const int HistoryShown = 3;
        private const float UnknownBand = 0.6f;

        private static readonly Vector2 A = new Vector2(0.5f, 0.25f);    // +u: toward the right corner
        private static readonly Vector2 B = new Vector2(-0.5f, 0.25f);   // +v: toward the left corner
        private static readonly Color Ink = new Color32(59, 42, 32, 255);
        private static readonly Color Rim = new Color32(150, 128, 98, 255);
        private static readonly Color Grass = new Color32(126, 180, 84, 255);
        private static readonly Color UnknownColour = new Color32(66, 52, 48, 255);
        private static readonly Color Timber = new Color32(150, 98, 54, 255);

        [SerializeField] private Material material;
        [SerializeField] private string groundLayer = "Ground";

        private GameRoot _root;
        private Mesh _interior, _shell;
        private TextMeshPro _workingLabel, _unknownLabel, _historyLabel;
        private string _key = "";
        private float _floor = -1f;      // displayed floor depth (smoothed)
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color> _cols = new List<Color>();
        private readonly List<int> _tris = new List<int>();

        public void EditorSetup(Material mat) => material = mat;

        // ------------------------------------------------------------------ geometry helpers (local space)

        /// <summary>A point on the block: u, v in cells from the front corner, z in world units (up positive).</summary>
        public static Vector3 Local(float u, float v, float z) => new Vector3(u * A.x + v * B.x, u * A.y + v * B.y + z, 0f);

        public Vector3 World(float u, float v, float z) => transform.TransformPoint(Local(u, v, z));

        private float ShaftMin => Inset;
        private float ShaftMax => Footprint - Inset;

        /// <summary>Where a miner stands on the shaft floor (near the back walls, so the rim never hides them).</summary>
        public Vector3 FloorSpot(int index)
        {
            var spots = new[] { new Vector2(2.55f, 2.1f), new Vector2(2.0f, 2.6f), new Vector2(2.7f, 2.65f) };
            var s = spots[Mathf.Abs(index) % spots.Length];
            return World(s.x, s.y, -FloorDepth);
        }

        public Vector3 LadderTop => World(1.55f, ShaftMax + 0.05f, 0f);
        public Vector3 LadderBottom => World(1.55f, ShaftMax - 0.25f, -FloorDepth);

        /// <summary>Depth of the displayed shaft floor below the rim (world units).</summary>
        public float FloorDepth => _floor < 0f ? 0.5f : _floor;

        /// <summary>For tests: what the faces currently show.</summary>
        public int ShownDepth { get; private set; }
        public int HistoryBandsShown { get; private set; }
        public int TunnelStubs { get; private set; }
        public float SectionProgress { get; private set; }

        // ------------------------------------------------------------------ lifecycle

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _interior = MakeChild("Interior", 10);
            _shell = MakeChild("Shell", 20);
            _workingLabel = Label("WorkingLabel");
            _unknownLabel = Label("UnknownLabel");
            _historyLabel = Label("HistoryLabel");
        }

        private Mesh MakeChild(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.sortingLayerName = groundLayer;
            mr.sortingOrder = order;
            var mesh = new Mesh { name = "MineCutaway" + name };
            mesh.MarkDynamic();
            mf.sharedMesh = mesh;
            return mesh;
        }

        private TextMeshPro Label(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var t = go.AddComponent<TextMeshPro>();
            t.fontSize = 2.2f;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color32(250, 242, 226, 255);
            t.outlineWidth = 0.25f;
            t.outlineColor = new Color32(59, 42, 32, 255);
            t.rectTransform.sizeDelta = new Vector2(5f, 0.5f);
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            t.sortingLayerID = SortingLayer.NameToID("Overlay");
            return t;
        }

        private void Update()
        {
            var sim = _root.Services.Sites.Get(SiteId);
            if (sim == null) return;
            var s = sim.Record;
            var progress = s.SectionDurabilityMicro > 0 ? Mathf.Clamp01((float)s.SectionDoneMicro / s.SectionDurabilityMicro) : 0f;
            var history = Mathf.Min(s.Depth - 1, HistoryShown) * HistoryBand + (s.Depth - 1 > HistoryShown ? HistoryBand : 0f);
            // The floor sits inside the working layer and drops as the section is worked (never deeper than the opening shows).
            var target = history + 0.12f + 0.45f * progress;
            if (_floor < 0f || Feel.ReducedMotion) _floor = target;
            else if (target < _floor - 0.05f) _floor = target;                       // a new section starts back at the top
            else _floor = Mathf.MoveTowards(_floor, target, Time.deltaTime * 0.8f);   // otherwise the floor sinks smoothly

            var key = $"{s.Depth}|{s.K}|{_floor:0.000}|{sim.CanGoDeeper}|{s.Revealed.Count}";
            if (key == _key) return;
            _key = key;
            ShownDepth = s.Depth;
            TunnelStubs = Mathf.Min(s.K, 3);
            SectionProgress = progress;
            Rebuild(sim, history);
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>Warm earth bases that alternate with depth, so neighbouring layers always read as separate bands.</summary>
        private static readonly Color[] Earth =
        {
            new Color32(196, 150, 104, 255), new Color32(176, 112, 76, 255), new Color32(150, 104, 80, 255),
            new Color32(184, 128, 84, 255), new Color32(132, 94, 76, 255), new Color32(164, 118, 88, 255),
        };

        private Color Mix(Composition c, int depth)
        {
            if (c == null) return UnknownColour;
            var ore = Color.black;
            foreach (var w in c.Weights)
                ore += (_root.Services.Catalogue.TryGet<ResourceDefinition>(w.Key, out var r) ? r.MapTint : Color.gray) * (float)w.Value;
            // Earth with an ore cast: a copper seam leans orange, coal measures darker, moonsilver paler.
            var col = Color.Lerp(Earth[(Mathf.Max(1, depth) - 1) % Earth.Length], ore, 0.45f);
            col.a = 1f;
            return col;
        }

        private struct Band
        {
            public float Top, Bottom;
            public Color Colour;
            public Composition Comp;
        }

        private void Rebuild(SiteSimulator sim, float history)
        {
            var s = sim.Record;
            var bands = new List<Band>();
            var z = 0f;
            var firstShown = Mathf.Max(1, s.Depth - HistoryShown);
            if (firstShown > 1)
            {
                bands.Add(new Band { Top = z, Bottom = z - HistoryBand, Colour = Color.Lerp(Rim, Ink, 0.25f) });   // older depths, merged
                z -= HistoryBand;
            }
            for (var d = firstShown; d < s.Depth; d++)
            {
                s.Revealed.TryGetValue(d, out var comp);
                bands.Add(new Band { Top = z, Bottom = z - HistoryBand, Colour = Mix(comp, d), Comp = comp });
                z -= HistoryBand;
            }
            var workBottom = -(FaceHeight - UnknownBand);
            bands.Add(new Band { Top = z, Bottom = workBottom, Colour = Mix(s.Composition, s.Depth), Comp = s.Composition });
            bands.Add(new Band { Top = workBottom, Bottom = -FaceHeight, Colour = UnknownColour });
            HistoryBandsShown = bands.Count - 2;

            // --- interior (drawn first): far shaft walls striped by every layer they cut, the floor, tunnel stubs, ladder
            Begin();
            var floor = -_floor;
            float lo = ShaftMin, hi = ShaftMax;
            foreach (var b in bands)
            {
                if (b.Top <= floor) break;
                var bottom = Mathf.Max(b.Bottom, floor);
                Quad(Local(lo, hi, b.Top), Local(hi, hi, b.Top), Local(hi, hi, bottom), Local(lo, hi, bottom), Shade(b.Colour, 0.62f));   // back-left wall
                Quad(Local(hi, lo, b.Top), Local(hi, hi, b.Top), Local(hi, hi, bottom), Local(hi, lo, bottom), Shade(b.Colour, 0.8f));    // back-right wall
                Line(Local(lo, hi, bottom), Local(hi, hi, bottom), 0.025f, Shade(Ink, 1f, 0.6f));
                Line(Local(hi, lo, bottom), Local(hi, hi, bottom), 0.025f, Shade(Ink, 1f, 0.6f));
            }
            var work = bands[bands.Count - 2].Colour;
            Quad(Local(lo, lo, floor), Local(hi, lo, floor), Local(hi, hi, floor), Local(lo, hi, floor), Shade(work, 1.08f));
            Specks(lo + 0.2f, lo + 0.2f, hi - 0.2f, hi - 0.2f, floor, bands[bands.Count - 2].Comp, s.Depth * 31 + 7, 10);
            // "Continue here" opens a side tunnel off the same face (GDD 6).
            for (var i = 0; i < Mathf.Min(s.K, 3); i++)
            {
                var v0 = lo + 0.35f + i * 0.75f;
                Quad(Local(hi, v0, floor + 0.42f), Local(hi, v0 + 0.45f, floor + 0.42f), Local(hi, v0 + 0.45f, floor), Local(hi, v0, floor), Shade(Ink, 0.75f));
            }
            // Ladder up the back-left wall: works at any depth (no terraces).
            for (var side = -1; side <= 1; side += 2)
                Line(Local(1.55f + side * 0.12f, hi - 0.02f, 0.06f), Local(1.55f + side * 0.12f, hi - 0.02f, floor), 0.035f, Timber);
            for (var rz = floor + 0.12f; rz < 0f; rz += 0.16f)
                Line(Local(1.43f, hi - 0.02f, rz), Local(1.67f, hi - 0.02f, rz), 0.025f, Shade(Timber, 1.15f));
            End(_interior);

            // --- shell (drawn over the interior and anything inside the shaft): rim with the shaft opening, front faces
            Begin();
            float n = Footprint;
            Quad(Local(0, 0, 0), Local(n, 0, 0), Local(n, lo, 0), Local(0, lo, 0), Rim);          // front-right strip
            Quad(Local(0, 0, 0), Local(lo, 0, 0), Local(lo, n, 0), Local(0, n, 0), Rim);          // front-left strip
            Quad(Local(hi, lo, 0), Local(n, lo, 0), Local(n, n, 0), Local(hi, n, 0), Rim);        // back-right strip
            Quad(Local(lo, hi, 0), Local(hi, hi, 0), Local(hi, n, 0), Local(lo, n, 0), Rim);      // back-left strip
            // grass lip on the outer edge so the block meets the town tiles
            Quad(Local(0, 0, 0.001f), Local(n, 0, 0.001f), Local(n, 0.18f, 0.001f), Local(0, 0.18f, 0.001f), Grass);
            Quad(Local(0, 0, 0.001f), Local(0.18f, 0, 0.001f), Local(0.18f, n, 0.001f), Local(0, n, 0.001f), Grass);
            foreach (var b in bands)
            {
                Quad(Local(0, 0, b.Top), Local(0, n, b.Top), Local(0, n, b.Bottom), Local(0, 0, b.Bottom), Shade(b.Colour, 0.95f));   // front-left face
                Quad(Local(0, 0, b.Top), Local(n, 0, b.Top), Local(n, 0, b.Bottom), Local(0, 0, b.Bottom), Shade(b.Colour, 0.74f));   // front-right face
                Line(Local(0, n, b.Bottom), Local(0, 0, b.Bottom), 0.03f, Shade(Ink, 1f, 0.7f));
                Line(Local(0, 0, b.Bottom), Local(n, 0, b.Bottom), 0.03f, Shade(Ink, 1f, 0.7f));
                if (b.Top - b.Bottom > 0.3f) FaceSpecks(b, s.Depth * 13 + (int)(b.Top * 100));
            }
            // outlines: outer silhouette, shaft opening, vertical corner
            Line(Local(0, n, 0), Local(0, 0, 0), 0.05f, Ink);
            Line(Local(0, 0, 0), Local(n, 0, 0), 0.05f, Ink);
            Line(Local(0, n, 0), Local(0, n, -FaceHeight), 0.05f, Ink);
            Line(Local(n, 0, 0), Local(n, 0, -FaceHeight), 0.05f, Ink);
            Line(Local(0, 0, 0), Local(0, 0, -FaceHeight), 0.05f, Ink);
            Line(Local(0, n, -FaceHeight), Local(0, 0, -FaceHeight), 0.05f, Ink);
            Line(Local(0, 0, -FaceHeight), Local(n, 0, -FaceHeight), 0.05f, Ink);
            Line(Local(lo, lo, 0), Local(hi, lo, 0), 0.04f, Ink);
            Line(Local(lo, lo, 0), Local(lo, hi, 0), 0.04f, Ink);
            Line(Local(hi, lo, 0), Local(hi, hi, 0), 0.04f, Ink);
            Line(Local(lo, hi, 0), Local(hi, hi, 0), 0.04f, Ink);
            End(_shell);

            // --- labels on the front-right face, centred, so they never run off screen
            var right = Local(n * 0.5f, 0, 0);
            var workMid = (bands[bands.Count - 2].Top + bands[bands.Count - 2].Bottom) * 0.5f;
            _workingLabel.transform.localPosition = right + new Vector3(0f, workMid, 0f);
            _workingLabel.text = $"Depth {s.Depth} · working" + (s.K > 0 ? $" · section {s.K + 1}" : "");
            _unknownLabel.transform.localPosition = right + new Vector3(0f, -FaceHeight + UnknownBand * 0.5f, 0f);
            _unknownLabel.text = sim.CanGoDeeper ? $"Depth {s.Depth + 1} · unknown" : "No deeper depths charted";
            _historyLabel.gameObject.SetActive(s.Depth > 1);
            _historyLabel.transform.localPosition = right + new Vector3(0f, -history - 0.18f, 0f);
            _historyLabel.fontSize = 1.7f;
            _historyLabel.text = s.Depth == 2 ? "Depth 1 · closed" : $"Depths 1–{s.Depth - 1} · closed";
        }

        private void FaceSpecks(Band b, int seed)
        {
            var rng = new System.Random(seed);
            for (var i = 0; i < 26; i++)
            {
                var t = (float)rng.NextDouble();
                var z = Mathf.Lerp(b.Top - 0.08f, b.Bottom + 0.08f, (float)rng.NextDouble());
                var onLeft = rng.Next(2) == 0;
                var p = onLeft ? Local(0, 0.2f + t * (Footprint - 0.4f), z) : Local(0.2f + t * (Footprint - 0.4f), 0, z);
                var c = SpeckColour(b, rng, onLeft ? 0.8f : 0.62f);
                Diamond(p + new Vector3(0, 0, -0.001f), 0.06f + (float)rng.NextDouble() * 0.04f, c);
            }
        }

        private void Specks(float u0, float v0, float u1, float v1, float z, Composition comp, int seed, int count)
        {
            var rng = new System.Random(seed);
            for (var i = 0; i < count; i++)
            {
                var p = Local(Mathf.Lerp(u0, u1, (float)rng.NextDouble()), Mathf.Lerp(v0, v1, (float)rng.NextDouble()), z);
                Diamond(p, 0.07f, SpeckColour(new Band { Comp = comp, Colour = Rim }, rng, 1f));
            }
        }

        /// <summary>Ore flecks drawn from the layer's own composition, so a copper seam actually looks coppery.</summary>
        private Color SpeckColour(Band b, System.Random rng, float shade)
        {
            if (b.Comp != null && b.Comp.Weights.Count > 0)
            {
                var u = rng.NextDouble();
                foreach (var w in b.Comp.Weights)
                {
                    u -= w.Value;
                    if (u <= 0 && _root.Services.Catalogue.TryGet<ResourceDefinition>(w.Key, out var r)) return Shade(r.MapTint, shade * 1.05f);
                }
            }
            return Shade(b.Colour, shade * 0.8f);
        }

        // ------------------------------------------------------------------ mesh building

        private void Begin()
        {
            _verts.Clear();
            _cols.Clear();
            _tris.Clear();
        }

        private void End(Mesh m)
        {
            m.Clear();
            m.SetVertices(_verts);
            m.SetColors(_cols);
            m.SetTriangles(_tris, 0);
            m.RecalculateBounds();
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            var i = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c); _verts.Add(d);
            // Vertex colours are authored in sRGB like the sprites; the project renders in linear space.
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) col = col.linear;
            for (var k = 0; k < 4; k++) _cols.Add(col);
            // both windings, so faces never cull away whatever their orientation
            _tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3, i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        private void Line(Vector3 a, Vector3 b, float width, Color col)
        {
            var dir = (b - a).normalized;
            var n = new Vector3(-dir.y, dir.x, 0f) * (width * 0.5f);
            Quad(a + n, b + n, b - n, a - n, col);
        }

        private void Diamond(Vector3 c, float r, Color col) =>
            Quad(c + new Vector3(0, r * 0.6f), c + new Vector3(r, 0), c + new Vector3(0, -r * 0.6f), c + new Vector3(-r, 0), col);

        private static Color Shade(Color c, float k, float alpha = 1f) => new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), alpha);
    }
}
