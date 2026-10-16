using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// A build project on the road (the journey, step 7): a ravine to bridge, a flood to cross. The land is a gap until it
    /// is built; the sign at its near side offers the build (its materials from stock). Built, the gap is filled with the
    /// bridge's planks or the causeway's stones and can be walked.
    /// </summary>
    public sealed class GateProjectSpot : Inspectable
    {
        [SerializeField] private int gate = -1;
        [SerializeField] private Tilemap ground;
        [SerializeField] private TileBase fill;

        private GameRoot _root;
        private SpriteRenderer _sign;
        private Collider2D _collider;
        private readonly List<Vector3Int> _gap = new List<Vector3Int>();

        public int Gate => gate;
        public bool Available { get; private set; }
        public IReadOnlyList<Vector3Int> GapCells => _gap;

        public override string ContextVerb => $"Build {_root?.Services.Camp.ProjectName(gate).ToLowerInvariant()}";
        public override bool OpensOwnPanel => true;
        public override bool IsContextAvailable => Available;
        public override float ContextPriority => 2f;   // the road's way on: ahead of the trees and rocks within a step of it

        public void EditorSetup(int gateIndex, Tilemap groundMap, TileBase fillTile)
        {
            gate = gateIndex;
            ground = groundMap;
            fill = fillTile;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _sign = GetComponent<SpriteRenderer>();
            _collider = GetComponent<Collider2D>();
            // The gap's cells: around the gate on the road, wherever the land is a project's gap.
            var road = _root.Services.Catalogue.CurrentCamp;
            var ridge = road.Ridge;
            var (gx, gy) = ridge.PointAt(ridge.Gates[gate].S);
            var reach = (int)(ridge.HalfWidth + ridge.WidthWobble + ridge.EdgeNoise + 6);
            for (var x = (int)gx - reach; x <= (int)gx + reach; x++)
            for (var y = (int)gy - reach; y <= (int)gy + reach; y++)
                if (ridge.InProjectGap(x, y) && ridge.GateIndexAt(x, y) == gate) _gap.Add(new Vector3Int(x, y, 0));
            var name = _root.Services.Camp.ProjectName(gate);
            var sb = new StringBuilder($"The road needs a {name.ToLowerInvariant()} here. It takes ");
            var first = true;
            foreach (var c in _root.Services.Camp.ProjectCost(gate))
            {
                sb.Append(first ? "" : ", ").Append(c.quantity).Append(' ').Append(c.resource.DisplayName);
                first = false;
            }
            EditorSetup($"Build the {name.ToLowerInvariant()}", sb.Append(" from your stock.").ToString(), "");
            _root.Services.Camp.ProjectBuilt += OnBuilt;
            Apply();
        }

        private void OnDestroy()
        {
            if (_root != null && _root.Services != null) _root.Services.Camp.ProjectBuilt -= OnBuilt;
        }

        private void OnBuilt(int g)
        {
            if (g == gate && this != null) Apply();
        }

        private void Apply()
        {
            var built = _root.Services.Camp.IsBuilt(gate);
            if (built && ground != null && fill != null)
                foreach (var c in _gap) ground.SetTile(c, fill);
            if (_sign != null) _sign.enabled = !built;
            if (_collider != null) _collider.enabled = !built;
        }

        private void Update()
        {
            var player = SandboxSceneController.Current != null ? SandboxSceneController.Current.Player : null;
            Available = player != null && !_root.Services.Camp.IsBuilt(gate) && Vector2.Distance(player.position, transform.position) < 3f;
        }

        public override void OnInspected()
        {
            if (_root == null || _root.Services.Camp.IsBuilt(gate)) return;
            var why = _root.Services.Camp.Build(gate);
            _root.Hud.ShowToast(why ?? $"The {_root.Services.Camp.ProjectName(gate).ToLowerInvariant()} is built: the road is open.");
        }
    }
}
