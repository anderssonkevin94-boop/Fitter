using System.Collections.Generic;
using Ravenholm.Domain.Camp;
using TMPro;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// A station standing at the camp (the journey, step 2, 2026-10-15): keeps the building, its delivery marker and its
    /// output pad together where the stations service says it stands, hides it while packed into its kit, and shows the
    /// short build after it is set up somewhere (a scaffold, a faded building and a timer; it takes nothing meanwhile).
    /// </summary>
    [DefaultExecutionOrder(100)]   // after the markers and pads have registered themselves
    public sealed class StationSiteView : MonoBehaviour
    {
        [SerializeField] private string stationId = "";
        [SerializeField] private Vector2Int footprint = new Vector2Int(2, 2);
        [Tooltip("The station's delivery marker and output pad: they move with it.")]
        [SerializeField] private List<Transform> attached = new List<Transform>();
        [SerializeField] private Sprite scaffoldSprite;

        public static readonly Dictionary<string, StationSiteView> All = new Dictionary<string, StationSiteView>();

        private GameRoot _root;
        private readonly List<Vector3> _offsets = new List<Vector3>();
        private Vector3 _laidOut;
        private SpriteRenderer _body, _scaffold;
        private Collider2D _tap;
        private TextMeshPro _timer;
        private bool _wasBuilding;

        public string StationId => stationId;
        public Vector2Int Footprint => footprint;
        public Collider2D TapCollider => _tap;

        public void EditorSetup(string id, Vector2Int cells, List<Transform> moveWith, Sprite scaffold)
        {
            stationId = id;
            footprint = cells;
            attached = moveWith;
            scaffoldSprite = scaffold;
        }

        private void Awake()
        {
            All[stationId] = this;
            _body = GetComponent<SpriteRenderer>();
            _tap = GetComponent<BoxCollider2D>();
        }

        private void OnDestroy()
        {
            if (All.TryGetValue(stationId, out var v) && v == this) All.Remove(stationId);
            if (_root != null) _root.Services.Stations.Changed -= Apply;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _laidOut = transform.position;
            foreach (var t in attached) _offsets.Add(t != null ? t.position - transform.position : Vector3.zero);
            var (x, y) = CornerOf(_laidOut, footprint);
            _root.Services.Stations.RegisterDefault(stationId, x, y, footprint.x, footprint.y);
            _root.Services.Stations.OccupiedByOthers ??= OccupiedByOthers;
            _root.Services.Stations.Changed += Apply;
            Apply();
        }

        /// <summary>Cells the shop's lot or another building (not a station: the service knows those) stands on.</summary>
        private static bool OccupiedByOthers(int x, int y)
        {
            var world = CentreOf(x, y, Vector2Int.one);
            if (ShopView.Current != null && ShopView.Current.InLot(world)) return true;
            foreach (var c in Physics2D.OverlapPointAll(world, LayerMask.GetMask(PlayerMotor.BlockerLayerName)))
                if (c != null && !(c is EdgeCollider2D) && c.GetComponentInParent<StationSiteView>(true) == null) return true;
            return false;
        }

        /// <summary>The corner cell of a footprint centred on a world point.</summary>
        public static (int x, int y) CornerOf(Vector2 centre, Vector2Int cells)
        {
            var cx = centre.x + 2f * (centre.y - 0.25f);
            var cy = 2f * (centre.y - 0.25f) - centre.x;
            return (Mathf.RoundToInt(cx - (cells.x - 1) * 0.5f), Mathf.RoundToInt(cy - (cells.y - 1) * 0.5f));
        }

        /// <summary>The world point at the middle of a footprint with its corner at (x, y).</summary>
        public static Vector2 CentreOf(int x, int y, Vector2Int cells)
        {
            var cx = x + (cells.x - 1) * 0.5f;
            var cy = y + (cells.y - 1) * 0.5f;
            return new Vector2((cx - cy) * 0.5f, (cx + cy) * 0.25f + 0.25f);
        }

        private void Apply()
        {
            if (this == null) return;
            var site = _root.Services.Stations.Site(stationId);
            if (site == null) return;
            var standing = site.Standing;
            if (!standing)
                foreach (var t in attached)
                    if (t != null && t.GetComponent<ReceiverView>() is ReceiverView r && t.gameObject.activeInHierarchy) r.Reregister(false);   // packed: takes nothing
            gameObject.SetActive(standing);
            var at = site.Mode == StationSiteMode.Placed ? (Vector3)CentreOf(site.X, site.Y, footprint) : _laidOut;
            transform.position = at;
            for (var i = 0; i < attached.Count; i++)
            {
                if (attached[i] == null) continue;
                attached[i].gameObject.SetActive(standing);
                attached[i].position = at + _offsets[i];
            }
            Physics2D.SyncTransforms();
            _wasBuilding = !_wasBuilding;   // force the build look to refresh
            RefreshBuild(true);
        }

        private void Update() => RefreshBuild(false);

        /// <summary>While the build runs: the building faded behind a scaffold with a timer, taking nothing, not tappable.</summary>
        private void RefreshBuild(bool force)
        {
            if (_root == null) return;
            var stations = _root.Services.Stations;
            var site = stations.Site(stationId);
            var building = site != null && site.Standing && stations.Building(stationId);
            if (building != _wasBuilding || force)
            {
                _wasBuilding = building;
                if (_body != null) _body.color = building ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
                if (_tap != null) _tap.enabled = !building;
                foreach (var t in attached)
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    var receiver = t.GetComponent<ReceiverView>();
                    if (receiver != null) receiver.Reregister(!building);
                    var pad = t.GetComponent<WorldContainerView>();
                    if (pad != null) pad.Reregister();
                }
                Scaffold().enabled = building;
                Timer().gameObject.SetActive(building);
            }
            if (building) Timer().text = $"{Mathf.CeilToInt((1f - stations.BuildProgress(stationId)) * _root.Services.Balance.stationBuildSeconds)}s";
        }

        private SpriteRenderer Scaffold()
        {
            if (_scaffold != null) return _scaffold;
            var go = new GameObject("Scaffold");
            go.transform.SetParent(transform, false);
            _scaffold = go.AddComponent<SpriteRenderer>();
            _scaffold.sprite = scaffoldSprite;
            _scaffold.spriteSortPoint = SpriteSortPoint.Pivot;
            if (_body != null)
            {
                _scaffold.sharedMaterial = _body.sharedMaterial;
                _scaffold.sortingLayerID = _body.sortingLayerID;
                _scaffold.sortingOrder = _body.sortingOrder + 1;
            }
            return _scaffold;
        }

        private TextMeshPro Timer()
        {
            if (_timer != null) return _timer;
            var go = new GameObject("BuildTimer");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, (_body != null && _body.sprite != null ? _body.sprite.bounds.max.y : 2f) + 0.3f, 0f);
            _timer = go.AddComponent<TextMeshPro>();
            _timer.fontSize = 3.2f;
            _timer.fontStyle = FontStyles.Bold;
            _timer.alignment = TextAlignmentOptions.Center;
            _timer.color = new Color32(250, 242, 226, 255);
            _timer.outlineWidth = 0.25f;
            _timer.outlineColor = new Color32(59, 42, 32, 255);
            _timer.rectTransform.sizeDelta = new Vector2(3f, 0.6f);
            _timer.sortingLayerID = SortingLayer.NameToID("Overlay");
            return _timer;
        }
    }
}
