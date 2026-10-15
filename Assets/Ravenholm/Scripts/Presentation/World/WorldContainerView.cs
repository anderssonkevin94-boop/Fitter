using System;
using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Logistics;
using Ravenholm.Services;
using TMPro;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// View of a world container (raw pile or output pad). Registers its id, position and test
    /// fixture with the logistics service, then draws a bounded number of representative pieces
    /// plus a count badge. It owns nothing: quantities live in the domain.
    /// </summary>
    public sealed class WorldContainerView : MonoBehaviour
    {
        [Serializable]
        public struct ResourceAmount
        {
            public ResourceDefinition resource;
            [Min(0)] public int quantity;
        }

        private static readonly Dictionary<string, WorldContainerView> Registry = new Dictionary<string, WorldContainerView>();

        [Tooltip("Stable container ID saved in player files, e.g. 'container.mine_staging'.")]
        [SerializeField] private string containerId = "";
        [SerializeField] private ContainerKind kind;
        [Tooltip("Pile footprint radius; pickup reach is this plus the BalanceProfile pickup radius.")]
        [SerializeField] private float radius = 0.45f;

        [Header("PHASE 1 TEST FIXTURE: starting contents")]
        [SerializeField] private List<ResourceAmount> fixtureMaterials = new List<ResourceAmount>();
        [SerializeField] private List<ItemDefinition> fixtureItems = new List<ItemDefinition>();

        [Tooltip("Shown in the badge as count/capacity (e.g. site staging, GDD 3: 96). 0 = no capacity shown.")]
        [SerializeField] private int displayCapacity;
        [SerializeField] private int maxPieces = 9;
        [SerializeField] private float pieceSpacing = 0.22f;

        private readonly List<SpriteRenderer> _pieces = new List<SpriteRenderer>();
        private readonly List<CarryUnit> _units = new List<CarryUnit>();
        private TextMeshPro _badge;
        private Transform _pileRoot;
        private LogisticsService _service;

        public string ContainerId => containerId;

        public static bool TryGet(string id, out WorldContainerView view) => Registry.TryGetValue(id, out view);

        /// <summary>World position of the topmost visible piece (pickup arcs start here).</summary>
        public Vector3 TopPiecePosition => _pieces.Count > 0 && _pieces[0].gameObject.activeSelf
            ? LastActivePiece().position
            : transform.position + Vector3.up * 0.2f;

        public void EditorSetCapacity(int capacity) => displayCapacity = capacity;

        public void EditorSetup(string id, ContainerKind k, float r, List<ResourceAmount> materials, List<ItemDefinition> items)
        {
            containerId = id;
            kind = k;
            radius = r;
            fixtureMaterials = materials ?? new List<ResourceAmount>();
            fixtureItems = items ?? new List<ItemDefinition>();
        }

        private void OnEnable() => Registry[containerId] = this;

        private void OnDisable()
        {
            if (Registry.TryGetValue(containerId, out var v) && v == this) Registry.Remove(containerId);
            if (_service == null) return;
            _service.System.Changed -= Refresh;
            _service.Reloaded -= Refresh;
        }

        private void Start()
        {
            var root = GameRoot.Instance;
            if (root == null) { enabled = false; return; }
            _service = root.Services.Logistics;
            BuildVisuals();

            var fixture = new ContainerFixture();
            foreach (var m in fixtureMaterials)
                if (m.resource != null && m.quantity > 0) fixture.Materials.Add(new KeyValuePair<string, int>(m.resource.Id, m.quantity));
            foreach (var i in fixtureItems)
                if (i != null) fixture.ItemDefinitions.Add(i.Id);

            var p = transform.position;
            _service.RegisterContainer(containerId, kind, p.x, p.y, radius, fixture);
            _service.System.Changed += Refresh;
            _service.Reloaded += Refresh;
            Refresh();
        }

        /// <summary>Tells the logistics where the pile is now (its station moved).</summary>
        public void Reregister()
        {
            if (_service == null) return;
            var p = transform.position;
            _service.MoveContainer(containerId, p.x, p.y);
        }

        private void BuildVisuals()
        {
            _pileRoot = new GameObject("Pieces").transform;
            _pileRoot.SetParent(transform, false);
            for (var i = 0; i < maxPieces; i++)
            {
                var go = new GameObject($"Piece{i}");
                go.transform.SetParent(_pileRoot, false);
                // Pyramid-ish pile: rows of 3, 2, 1... stacked upward.
                int row = 0, idx = i, rowSize = 3;
                while (idx >= rowSize && rowSize > 1) { idx -= rowSize; row++; rowSize--; }
                var x = (idx - (rowSize - 1) * 0.5f) * pieceSpacing;
                go.transform.localPosition = new Vector3(x, 0.05f + row * 0.13f, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.spriteSortPoint = SpriteSortPoint.Pivot;
                sr.sortingOrder = 2 + i;
                go.SetActive(false);
                _pieces.Add(sr);
            }

            var badgeGo = new GameObject("CountBadge");
            badgeGo.transform.SetParent(transform, false);
            badgeGo.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            _badge = badgeGo.AddComponent<TextMeshPro>();
            _badge.fontSize = 2.2f;
            _badge.fontStyle = FontStyles.Bold;
            _badge.alignment = TextAlignmentOptions.Center;
            _badge.color = new Color32(250, 242, 226, 255);
            _badge.outlineColor = new Color32(59, 42, 32, 255);
            _badge.outlineWidth = 0.3f;
            _badge.rectTransform.sizeDelta = new Vector2(1.2f, 0.4f);
            _badge.sortingOrder = 40;
        }

        private void Refresh()
        {
            if (_service == null || !_service.System.State.Containers.TryGetValue(containerId, out var c)) return;

            _units.Clear();
            foreach (var item in c.Items) _units.Add(CarryUnit.Item(item));
            foreach (var kv in c.Materials)
                for (var n = 0; n < kv.Value && _units.Count < maxPieces; n++) _units.Add(CarryUnit.Material(kv.Key));

            for (var i = 0; i < _pieces.Count; i++)
            {
                var show = i < _units.Count && i < maxPieces;
                _pieces[i].gameObject.SetActive(show);
                if (show) _pieces[i].sprite = UnitVisuals.StackSprite(_units[i]);
            }
            var total = c.UnitCount;
            _badge.text = displayCapacity > 0 ? $"{total}/{displayCapacity}" : total > 0 ? total.ToString() : "";
            _badge.color = displayCapacity > 0 && total >= displayCapacity ? new Color32(255, 150, 120, 255) : new Color32(250, 242, 226, 255);
        }

        private Transform LastActivePiece()
        {
            for (var i = _pieces.Count - 1; i >= 0; i--)
                if (_pieces[i].gameObject.activeSelf) return _pieces[i].transform;
            return transform;
        }
    }
}
