using System.Collections.Generic;
using Ravenholm.Domain.Sites;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The switchback pass seen from the road (Kevin, 2026-10-16: "a nice view"). Only the road the supplier is on is drawn
    /// as real ground: the stretch past the next hairpin is hidden (its ground, trees and cliffs), so it never stands in
    /// front of the view as a floating block; the layered valley (<see cref="ParallaxBackdrop"/>) shows there instead. Near
    /// the hairpin the next stretch appears, ready to walk onto. The road's layout is unchanged. Presentation only.
    /// </summary>
    public sealed class PassView : MonoBehaviour
    {
        public static PassView Current { get; private set; }

        [SerializeField] private Tilemap ground;
        [Tooltip("Cells before a hairpin from where the stretch past it appears.")]
        [SerializeField] private float revealCells = 10f;

        private RidgeLayout _ridge;
        private readonly List<(Vector3Int cell, double s)> _tiles = new List<(Vector3Int, double)>();
        private double _hiddenFrom = double.MaxValue;
        private bool _shown;

        /// <summary>The leg (0-based) the supplier is on now, and from where along the line the pass is hidden.</summary>
        public int CurrentLeg { get; private set; } = -1;
        public double HiddenFrom => _hiddenFrom;

        /// <summary>Changes whenever what is hidden changes (the views redraw on it).</summary>
        public int Version { get; private set; }

        public void EditorSetup(Tilemap groundMap) => ground = groundMap;

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
            if (ground != null)
            {
                var b = ground.cellBounds;
                foreach (var c in b.allPositionsWithin)
                    if (ground.HasTile(c)) _tiles.Add((c, _ridge.Locate(c.x, c.y).s));
            }
        }

        /// <summary>Whether a cell is hidden now (the pass past the next hairpin: its ground, trees and cliffs).</summary>
        public static bool Painted(Vector3Int c) =>
            Current != null && Current.isActiveAndEnabled && Current._ridge != null && Current._ridge.Locate(c.x, c.y).s >= Current._hiddenFrom;

        /// <summary>The same as a key for views that cache what they draw.</summary>
        public static int StateKey => Current != null && Current.isActiveAndEnabled ? Current.Version : -1;

        private void LateUpdate()
        {
            var player = SandboxSceneController.Current != null ? SandboxSceneController.Current.Player : null;
            if (player == null) return;
            var c = IsoMap.Cell(player.position);
            var s = _ridge.Locate(c.x, c.y).s;
            var leg = _ridge.LegIndexAt(s);
            var turns = _ridge.TurnSpans;
            // Hidden from the next hairpin on; close to it, from the one after (the next stretch shown to walk onto).
            var near = leg < turns.Count && s >= turns[leg].S0 - revealCells;
            var from = leg < turns.Count ? (near ? (leg + 1 < turns.Count ? turns[leg + 1].S0 : double.MaxValue) : turns[leg].S0) : double.MaxValue;
            if (leg == CurrentLeg && from == _hiddenFrom && _shown) return;
            CurrentLeg = leg;
            Show(from);
        }

        private void Show(double from)
        {
            _hiddenFrom = from;
            _shown = true;
            Version++;
            if (ground == null) return;
            foreach (var (c, s) in _tiles)
            {
                var hide = s >= _hiddenFrom;
                var col = ground.GetColor(c);
                if (hide == col.a < 0.5f) continue;
                ground.SetTileFlags(c, TileFlags.None);
                col.a = hide ? 0f : 1f;
                ground.SetColor(c, col);
            }
        }
    }
}
