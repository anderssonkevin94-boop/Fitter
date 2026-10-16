using System;
using System.Collections.Generic;
using Ravenholm.Domain.Sites;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The switchback pass seen from the road (Astra's pass pack, 2026-10-16). The leg the supplier is on and the next are
    /// the real ground; from the turn after the next leg onward the pass is shown as Astra's paintings (hazed toward the
    /// valley by distance), the real ground there hidden. The valley floor repeats behind everything (no flat colour past
    /// an edge), and the castle stands at the road's end, its gate on the road. Presentation only.
    /// </summary>
    public sealed class PassView : MonoBehaviour
    {
        [Serializable]
        public sealed class Region
        {
            public string key;
            public Sprite legUpRight, legUpLeft, turnLeft, turnRight;
        }

        public static PassView Current { get; private set; }

        [SerializeField] private List<Region> regions = new List<Region>();
        [SerializeField] private Sprite valleyFloor;
        [SerializeField] private Sprite castle;
        [SerializeField] private Tilemap ground;
        [SerializeField] private Material material;
        [SerializeField] private Color valleyColour = new Color32(112, 155, 92, 255);
        [Tooltip("How much a painted leg leans toward the valley colour, the first one shown and each further one.")]
        [SerializeField] private float hazeFirst = 0.18f, hazeStep = 0.1f;
        [Tooltip("Cells along a painted leg per section (the paintings' 8-tile strips).")]
        [SerializeField] private int sectionCells = 8;

        public const int PaintedOrder = -20000;   // Ground layer: above the valley and the horizon, below the real ground's tiles

        private RidgeLayout _ridge;
        private Transform _painted, _valleyRoot;
        private SpriteRenderer _valley, _castle;
        private readonly List<(Vector3Int cell, double s)> _tiles = new List<(Vector3Int, double)>();
        private readonly List<(SpriteRenderer sr, int leg, bool turn)> _modules = new List<(SpriteRenderer, int, bool)>();
        private double _paintedFrom = double.MaxValue;
        private int _shownLeg = -1;
        private Camera _camera;

        /// <summary>The leg (0-based) the supplier is on now, and from where along the line the pass is painted.</summary>
        public int CurrentLeg => _shownLeg;
        public double PaintedFrom => _paintedFrom;
        public int ModulesShown { get; private set; }

        public void EditorSetup(List<Region> regionArt, Sprite valley, Sprite castleArt, Tilemap groundMap, Material mat, Color valleyTint)
        {
            regions = regionArt;
            valleyFloor = valley;
            castle = castleArt;
            ground = groundMap;
            material = mat;
            valleyColour = valleyTint;
        }

        private void Awake() => Current = this;

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void Start()
        {
            var root = GameRoot.Instance;
            _ridge = root != null ? root.Services.Catalogue.CurrentCamp?.Ridge : null;
            if (_ridge == null || !_ridge.Switchback) { enabled = false; return; }
            _camera = Camera.main;
            BuildValley();
            BuildModules();
            BuildCastle();
            if (ground != null)
            {
                var b = ground.cellBounds;
                foreach (var c in b.allPositionsWithin)
                    if (ground.HasTile(c)) _tiles.Add((c, _ridge.Locate(c.x, c.y).s));
            }
        }

        /// <summary>Whether a cell is shown as painting now (its real ground, trees and cliffs hidden).</summary>
        public static bool Painted(Vector3Int c) =>
            Current != null && Current.isActiveAndEnabled && Current._ridge != null && Current._ridge.Locate(c.x, c.y).s >= Current._paintedFrom;

        private void LateUpdate()
        {
            var player = SandboxSceneController.Current != null ? SandboxSceneController.Current.Player : null;
            if (player != null)
            {
                var c = IsoMap.Cell(player.position);
                var leg = _ridge.LegIndexAt(_ridge.Locate(c.x, c.y).s);
                if (leg != _shownLeg) Show(leg);
            }
            FollowCamera();
        }

        // ------------------------------------------------------------------ the paintings

        private Region RegionOf(int leg) => regions.Count == 0 ? null : regions[Mathf.Clamp(leg, 0, regions.Count - 1)];

        private SpriteRenderer Module(string name, Sprite sprite, Vector2 at, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_painted, false);
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = "Ground";
            sr.sortingOrder = order;
            if (material != null) sr.sharedMaterial = material;
            return sr;
        }

        private Vector2 LineWorld(double s)
        {
            var (x, y) = _ridge.PointAt(s);
            return IsoMap.World((float)x, (float)y);
        }

        /// <summary>
        /// Every leg as 8-cell strips (pivot at a strip's start, on the centre line; the last strip of a leg set back to end
        /// where the leg ends), every turn from its entry (its painted drop baked in): placed by the paintings' anchors.
        /// </summary>
        private void BuildModules()
        {
            _painted = new GameObject("PassPaintings").transform;
            _painted.SetParent(transform, false);
            for (var leg = 0; leg < _ridge.LegSpans.Count; leg++)
            {
                var span = _ridge.LegSpans[leg];
                var region = RegionOf(leg);
                var sprite = span.AlongX ? region?.legUpRight : region?.legUpLeft;
                if (sprite == null) continue;
                var length = span.S1 - span.S0;
                var count = Math.Max(1, (int)Math.Ceiling(length / sectionCells));
                for (var k = 0; k < count; k++)
                {
                    var s = Math.Min(span.S0 + k * sectionCells, span.S1 - sectionCells);   // the last strip ends at the leg's end
                    // Further down the pass draws first: each leg behind the one before, its later strips behind its earlier.
                    var sr = Module($"leg{leg + 1}_{k}", sprite, LineWorld(Math.Max(span.S0, s)), PaintedOrder - leg * 100 - k);
                    _modules.Add((sr, leg, false));
                }
            }
            for (var t = 0; t < _ridge.TurnSpans.Count; t++)
            {
                var turn = _ridge.TurnSpans[t];
                var region = RegionOf(t);
                var sprite = turn.Left ? region?.turnLeft : region?.turnRight;
                if (sprite == null) continue;
                var sr = Module($"turn{t + 1}", sprite, LineWorld(turn.S0), PaintedOrder - t * 100 - 50);   // between its two legs
                _modules.Add((sr, t, true));
            }
        }

        private void BuildCastle()
        {
            if (castle == null) return;
            _castle = Module("Castle", castle, LineWorld(_ridge.Length), PaintedOrder - 1000);   // its gate on the road's end
            _castle.transform.SetParent(transform, true);
        }

        /// <summary>The supplier is on <paramref name="leg"/>: paint from the turn after the next leg; show the real ground before it.</summary>
        private void Show(int leg)
        {
            _shownLeg = leg;
            var firstPaintedTurn = leg + 1;
            _paintedFrom = firstPaintedTurn < _ridge.TurnSpans.Count ? _ridge.TurnSpans[firstPaintedTurn].S0 : double.MaxValue;
            ModulesShown = 0;
            foreach (var (sr, i, turn) in _modules)
            {
                var on = turn ? i >= firstPaintedTurn : i >= leg + 2;
                sr.enabled = on;
                if (!on) continue;
                ModulesShown++;
                var away = turn ? i - firstPaintedTurn : i - (leg + 2);
                var haze = Mathf.Clamp01(hazeFirst + hazeStep * away);
                sr.color = Color.Lerp(Color.white, valleyColour, haze);
            }
            if (ground != null)
            {
                foreach (var (c, s) in _tiles)
                {
                    var hide = s >= _paintedFrom;
                    var col = ground.GetColor(c);
                    if (hide == col.a < 0.5f) continue;
                    ground.SetTileFlags(c, TileFlags.None);
                    col.a = hide ? 0f : 1f;
                    ground.SetColor(c, col);
                }
            }
        }

        // ------------------------------------------------------------------ the valley floor

        private void BuildValley()
        {
            if (valleyFloor == null) return;
            _valleyRoot = new GameObject("ValleyFloor").transform;
            _valleyRoot.SetParent(transform, false);
            _valley = _valleyRoot.gameObject.AddComponent<SpriteRenderer>();
            _valley.sprite = valleyFloor;
            _valley.drawMode = SpriteDrawMode.Tiled;
            _valley.tileMode = SpriteTileMode.Continuous;
            _valley.sortingLayerName = "Ground";
            _valley.sortingOrder = ParallaxBackdrop.BaseOrder - 100;   // behind the horizon and everything else
            if (material != null) _valley.sharedMaterial = material;
            if (_camera != null) _camera.backgroundColor = valleyColour;
        }

        /// <summary>The valley floor fills the view, drifting a little slower than the land (it lies far below it).</summary>
        private void FollowCamera()
        {
            if (_valley == null || _camera == null) return;
            var cam = (Vector2)_camera.transform.position;
            var h = _camera.orthographicSize * 2f + 4f;
            var w = h * _camera.aspect + 4f;
            var tile = _valley.sprite.bounds.size;
            // Snap the drift to whole tiles round the camera, so the pattern slides at 60 % of the land's speed.
            var drift = cam * 0.6f;
            var snapped = new Vector2(Mathf.Repeat(drift.x, tile.x), Mathf.Repeat(drift.y, tile.y));
            _valleyRoot.position = new Vector3(cam.x - snapped.x, cam.y - snapped.y, 0f);
            _valley.size = new Vector2(w + tile.x * 2f, h + tile.y * 2f);
        }
    }
}
