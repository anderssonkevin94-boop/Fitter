using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// A build project on the road (the journey, step 7): a ravine to bridge, a flood to cross. The land is a gap until it
    /// is built; the sign at its near side offers the build (its materials from stock). Built, the gap is filled with the
    /// bridge's planks or the causeway's stones and can be walked. On gated levels (2026-10-17) one sign serves the level
    /// the company is on: it stands before that level's gate when the gate is a project, and is hidden otherwise.
    /// </summary>
    public sealed class GateProjectSpot : Inspectable
    {
        [SerializeField] private int gate = -1;
        [SerializeField] private Tilemap ground;
        [SerializeField] private TileBase fill;
        [Tooltip("Gated levels: the causeway's stones (the bridge's planks are Fill).")]
        [SerializeField] private TileBase fillStone;
        [Tooltip("Gated levels: this sign follows the company's level.")]
        [SerializeField] private bool levels;

        public static GateProjectSpot Current { get; private set; }

        private GameRoot _root;
        private SpriteRenderer _sign;
        private Collider2D _collider;
        private readonly List<Vector3Int> _gap = new List<Vector3Int>();
        private bool _active;

        public int Gate => gate;
        public bool Available { get; private set; }
        public bool Shown => _active;
        public IReadOnlyList<Vector3Int> GapCells => _gap;

        public override string ContextVerb => "Build";   // with the title: "Build the bridge"
        public override bool OpensOwnPanel => true;
        public override bool IsContextAvailable => Available;
        public override float ContextPriority => 2f;   // the road's way on: ahead of the trees and rocks within a step of it

        public void EditorSetup(int gateIndex, Tilemap groundMap, TileBase fillTile)
        {
            gate = gateIndex;
            ground = groundMap;
            fill = fillTile;
        }

        /// <summary>Gated levels: one sign for whichever level the company is on.</summary>
        public void EditorSetupLevels(Tilemap groundMap, TileBase planks, TileBase stones)
        {
            levels = true;
            gate = -1;
            ground = groundMap;
            fill = planks;
            fillStone = stones;
        }

        private void Awake()
        {
            if (levels) Current = this;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_root != null && _root.Services != null) _root.Services.Camp.ProjectBuilt -= OnBuilt;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _sign = GetComponent<SpriteRenderer>();
            _collider = GetComponent<Collider2D>();
            _root.Services.Camp.ProjectBuilt += OnBuilt;
            if (levels)
            {
                _root.Services.Camp.Moved += (_, __) => Place();
                _root.Services.Camp.Reloaded += Place;
            }
            Place();
        }

        /// <summary>Finds the gate's gap and stands the sign before it (on levels: the company's level's gate, if a project).</summary>
        public void Place()
        {
            if (this == null || _root == null) return;
            var camp = _root.Services.Camp;
            if (levels) gate = camp.Index;
            _gap.Clear();
            var name = camp.ProjectName(gate);
            _active = !string.IsNullOrEmpty(name);
            if (!_active)
            {
                if (_sign != null) _sign.enabled = false;
                if (_collider != null) _collider.enabled = false;
                return;
            }
            // The gap's cells: around the gate on the road, wherever the land is a project's gap.
            var ridge = levels ? camp.Ridge : _root.Services.Catalogue.CurrentCamp.Ridge;
            var (gx, gy) = ridge.PointAt(ridge.Gates[gate].S);
            var reach = (int)(ridge.HalfWidth + ridge.WidthWobble + ridge.EdgeNoise + 6);
            for (var x = (int)gx - reach; x <= (int)gx + reach; x++)
            for (var y = (int)gy - reach; y <= (int)gy + reach; y++)
                if (ridge.InProjectGap(x, y) && ridge.GateIndexAt(x, y) == gate) _gap.Add(new Vector3Int(x, y, 0));
            if (levels)
            {
                // On the road, a few cells before the gap.
                var s = ridge.Gates[gate].S - 5;
                var (px, py) = ridge.PointBeside(s, ridge.RoadOffsetAt(s));
                transform.position = IsoMap.World((float)px, (float)py);
            }
            var sb = new StringBuilder($"The road needs a {name.ToLowerInvariant()} here. It takes ");
            var first = true;
            foreach (var c in camp.ProjectCost(gate))
            {
                sb.Append(first ? "" : ", ").Append(c.quantity).Append(' ').Append(c.resource.DisplayName);
                first = false;
            }
            EditorSetup($"the {name.ToLowerInvariant()}", sb.Append(" from your stock.").ToString(), "");
            Apply();
        }

        private void OnBuilt(int g)
        {
            if (g == gate && this != null) Apply();
        }

        private TileBase FillFor(string project) => levels && project == "Causeway" && fillStone != null ? fillStone : fill;

        private void Apply()
        {
            if (!_active) return;
            var built = _root.Services.Camp.IsBuilt(gate);
            var tile = FillFor(_root.Services.Camp.ProjectName(gate));
            if (built && ground != null && tile != null)
            {
                foreach (var c in _gap)
                {
                    ground.SetTile(c, tile);
                    var drop = IsoMap.Drop(c);   // the pass: drawn as low as the ground around it
                    if (drop == 0f) continue;
                    ground.SetTileFlags(c, TileFlags.None);
                    ground.SetTransformMatrix(c, Matrix4x4.Translate(new Vector3(0f, -drop, 0f)));
                }
                if (levels && MineSlabView.Current != null) MineSlabView.Current.RefreshGround();   // the gap's cliffs go
            }
            if (_sign != null) _sign.enabled = !built;
            if (_collider != null) _collider.enabled = !built;
        }

        private void Update()
        {
            var player = SandboxSceneController.Current != null ? SandboxSceneController.Current.Player : null;
            Available = _active && player != null && !_root.Services.Camp.IsBuilt(gate) && Vector2.Distance(player.position, transform.position) < 3f;
        }

        public override void OnInspected()
        {
            if (_root == null || !_active || _root.Services.Camp.IsBuilt(gate)) return;
            var why = _root.Services.Camp.Build(gate);
            _root.Hud.ShowToast(why ?? $"The {_root.Services.Camp.ProjectName(gate).ToLowerInvariant()} is built: the road is open.");
        }
    }
}
