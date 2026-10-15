using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// A piece of Store furniture (GDD 4): shows the level the Store's station has reached and, in that level's slot
    /// boxes, a representative subset of stocked items (the racks' order: pinned, strongest, newest). Each item is
    /// scaled uniformly to fit its box; potions stand on the box's bottom, other pieces sit centred. The floor
    /// footprint that blocks walking follows the level (the display tables grow longer).
    /// </summary>
    public sealed class FurnitureView : MonoBehaviour
    {
        [SerializeField] private StoreFurnitureDefinition definition;
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private PolygonCollider2D footprint;

        private GameRoot _root;
        private readonly List<SpriteRenderer> _items = new List<SpriteRenderer>();
        private readonly List<TextMeshPro> _badges = new List<TextMeshPro>();
        private string _key = "";
        private int _shownLevel = -1;

        public int Level => _shownLevel;
        public int SlotCount => _items.Count;
        public int VisibleCount { get; private set; }
        public StoreFurnitureDefinition Definition => definition;

        /// <summary>The room's two floor axes, one tile each (front corner → right, front corner → left).</summary>
        public static readonly Vector2 AxisA = new Vector2(0.5f, 0.25f), AxisB = new Vector2(-0.5f, 0.25f);

        public void EditorSetup(StoreFurnitureDefinition def, SpriteRenderer sr, PolygonCollider2D blocker)
        {
            definition = def;
            visual = sr;
            footprint = blocker;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null || definition == null) { enabled = false; return; }
            Update();
        }

        private void Update()
        {
            var level = definition.LevelFrom != null ? _root.Services.Production.System.Level(definition.LevelFrom.Id) : 1;
            if (level != _shownLevel) ShowLevel(level);
            if (_items.Count == 0) { VisibleCount = 0; return; }

            var st = _root.Services.Logistics.System.State;
            var key = st.StoreItems.Count + ":" + (st.StoreItems.Count > 0 ? st.StoreItems[st.StoreItems.Count - 1] : "") + ":" + string.Join(",", st.PinnedDefinitions);
            if (key == _key) return;
            _key = key;
            var list = RackView.Representative(_root, definition.Shows);
            VisibleCount = Mathf.Min(list.Count, _items.Count);
            for (var i = 0; i < _items.Count; i++)
            {
                var on = i < list.Count && list[i].def.StackSprite != null;
                _items[i].gameObject.SetActive(on);
                if (!on) continue;
                _items[i].sprite = list[i].def.StackSprite;
                Fit(_items[i], i);
                _badges[i].text = list[i].count > 1 ? "×" + list[i].count : "";
            }
        }

        private readonly List<Rect> _boxes = new List<Rect>();

        private void ShowLevel(int level)
        {
            _shownLevel = level;
            _key = "";
            var l = definition.At(level);
            if (l == null) return;
            if (visual != null) visual.sprite = l.sprite;
            if (footprint != null)
            {
                var a = AxisA * (l.footprint.x * 0.5f);
                var b = AxisB * (l.footprint.y * 0.5f);
                footprint.enabled = l.footprint.x > 0f && l.footprint.y > 0f;
                footprint.points = new[] { -a - b, a - b, a + b, -a + b };
            }

            foreach (var sr in _items) Destroy(sr.gameObject);
            _items.Clear();
            _badges.Clear();
            _boxes.Clear();
            var ppu = definition.PixelsPerUnit;
            foreach (var s in l.slots)
            {
                // Pixels (top-left origin) → local units from the ground point (y up).
                _boxes.Add(new Rect((s.x - l.ground.x) / ppu, (l.ground.y - s.yMax) / ppu, s.width / ppu, s.height / ppu));
                var go = new GameObject("Slot");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 5;
                if (visual != null) sr.sharedMaterial = visual.sharedMaterial;
                _items.Add(sr);
                var badgeGo = new GameObject("Badge");
                badgeGo.transform.SetParent(transform, false);
                var t = badgeGo.AddComponent<TextMeshPro>();
                t.fontSize = 1.6f;
                t.fontStyle = FontStyles.Bold;
                t.alignment = TextAlignmentOptions.Center;
                t.color = new Color32(250, 242, 226, 255);
                t.outlineColor = new Color32(59, 42, 32, 255);
                t.outlineWidth = 0.3f;
                t.rectTransform.sizeDelta = new Vector2(0.6f, 0.3f);
                t.sortingOrder = 6;
                badgeGo.transform.SetParent(go.transform, true);
                _badges.Add(t);
                go.SetActive(false);
            }
        }

        /// <summary>Scales the item uniformly into its box: potions on the box's bottom, everything else centred.</summary>
        private void Fit(SpriteRenderer sr, int i)
        {
            var box = _boxes[i];
            var b = sr.sprite.bounds;   // local to the sprite's pivot
            var scale = Mathf.Min(box.width / Mathf.Max(b.size.x, 0.001f), box.height / Mathf.Max(b.size.y, 0.001f));
            sr.transform.localScale = Vector3.one * scale;
            var bottom = definition.Display == FurnitureDisplay.Potion;
            var target = bottom ? new Vector2(box.center.x, box.yMin) : box.center;
            var anchor = bottom ? new Vector2(b.center.x, b.min.y) : (Vector2)b.center;
            sr.transform.localPosition = target - anchor * scale;
            // The count sits at the box's lower right (badge size is independent of the item's scale).
            var badge = _badges[i].transform;
            badge.localScale = Vector3.one / Mathf.Max(scale, 0.001f);
            badge.position = transform.TransformPoint(new Vector3(box.xMax, box.yMin + 0.04f, 0f));
        }

        /// <summary>The level's slot boxes in local units (for tests).</summary>
        public IReadOnlyList<Rect> Boxes => _boxes;
    }
}
