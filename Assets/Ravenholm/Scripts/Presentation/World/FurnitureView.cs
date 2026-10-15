using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Shop;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// One placed piece of shop furniture (GDD 4, Shop Titans style): its level's art in its facing (facing B mirrors
    /// facing A until that art exists), the stock <see cref="ShopView"/> hands it, scaled uniformly into the level's slot
    /// boxes (potions stand on the box's bottom, everything else sits centred), and, on the floor, a footprint that
    /// blocks walking. Built and placed by <see cref="ShopView"/>; it never changes the shop itself.
    /// </summary>
    public sealed class FurnitureView : MonoBehaviour
    {
        /// <summary>The shop's two floor axes, one tile each (front corner → right, front corner → left).</summary>
        public static readonly Vector2 AxisA = new Vector2(0.5f, 0.25f), AxisB = new Vector2(-0.5f, 0.25f);

        private ShopFurnitureDefinition _def;
        private SpriteRenderer _visual;
        private PolygonCollider2D _footprint;
        private readonly List<SpriteRenderer> _items = new List<SpriteRenderer>();
        private readonly List<TextMeshPro> _badges = new List<TextMeshPro>();
        private readonly List<Rect> _boxes = new List<Rect>();
        private string _stockKey = "";

        public string PlacementId { get; private set; }
        public int Level { get; private set; }
        public Facing Facing { get; private set; }
        public bool Mirrored { get; private set; }
        public ShopFurnitureDefinition Definition => _def;
        public int SlotCount => _boxes.Count;
        public int VisibleCount { get; private set; }
        public IReadOnlyList<Rect> Boxes => _boxes;
        public SpriteRenderer Visual => _visual;

        /// <summary>Creates the piece's object under <paramref name="parent"/>.</summary>
        public static FurnitureView Create(Transform parent, ShopFurnitureDefinition def, Material material, int blockerLayer)
        {
            var go = new GameObject(def.DisplayName);
            go.transform.SetParent(parent, false);
            go.AddComponent<SortingGroup>();
            var view = go.AddComponent<FurnitureView>();
            view._def = def;
            var vis = new GameObject("Visual");
            vis.transform.SetParent(go.transform, false);
            view._visual = vis.AddComponent<SpriteRenderer>();
            view._visual.spriteSortPoint = SpriteSortPoint.Pivot;
            if (material != null) view._visual.sharedMaterial = material;
            if (!def.Wall)
            {
                var blocker = new GameObject("Footprint") { layer = blockerLayer };
                blocker.transform.SetParent(go.transform, false);
                view._footprint = blocker.AddComponent<PolygonCollider2D>();
            }
            go.AddComponent<SpriteOutline>();
            return view;
        }

        /// <summary>Shows a placement: its level and facing, at <paramref name="ground"/> (the footprint centre, or the wall anchor).</summary>
        public void Show(Placement p, Vector2 ground, (int a, int b) footprint)
        {
            PlacementId = p.Id;
            transform.position = ground;
            if (Level == p.Level && Facing == p.Facing && _visual.sprite != null) return;
            Level = p.Level;
            Facing = p.Facing;
            var level = _def.At(p.Level);
            var art = p.Facing == Facing.B && level.facingB.sprite != null ? level.facingB : level.facingA;
            Mirrored = p.Facing == Facing.B && level.facingB.sprite == null;
            _visual.sprite = art.sprite;
            _visual.flipX = Mirrored;

            if (_footprint != null)
            {
                var a = AxisA * (footprint.a * 0.5f);
                var b = AxisB * (footprint.b * 0.5f);
                _footprint.points = new[] { -a - b, a - b, a + b, -a + b };
            }

            foreach (var sr in _items) Destroy(sr.gameObject);
            _items.Clear();
            _badges.Clear();
            _boxes.Clear();
            _stockKey = "";
            var ppu = _def.PixelsPerUnit;
            foreach (var s in art.slots)
            {
                // Pixels (top-left origin) → local units from the ground point (y up); mirrored with the art.
                var x = (s.x - art.ground.x) / ppu;
                if (Mirrored) x = -x - s.width / ppu;
                _boxes.Add(new Rect(x, (art.ground.y - s.yMax) / ppu, s.width / ppu, s.height / ppu));
                var go = new GameObject("Slot");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 5;
                sr.sharedMaterial = _visual.sharedMaterial;
                _items.Add(sr);
                var badgeGo = new GameObject("Badge");
                badgeGo.transform.SetParent(go.transform, false);
                var t = badgeGo.AddComponent<TextMeshPro>();
                t.fontSize = 1.6f;
                t.fontStyle = FontStyles.Bold;
                t.alignment = TextAlignmentOptions.Center;
                t.color = new Color32(250, 242, 226, 255);
                t.outlineColor = new Color32(59, 42, 32, 255);
                t.outlineWidth = 0.3f;
                t.rectTransform.sizeDelta = new Vector2(0.6f, 0.3f);
                t.sortingOrder = 6;
                _badges.Add(t);
                go.SetActive(false);
            }
        }

        /// <summary>Fills the slots with these items (one per slot, in order) and their counts.</summary>
        public void SetStock(IReadOnlyList<(ItemDefinition def, int count)> stock)
        {
            var key = new System.Text.StringBuilder();
            foreach (var s in stock) key.Append(s.def.Id).Append('x').Append(s.count).Append(',');
            if (key.ToString() == _stockKey) return;
            _stockKey = key.ToString();
            VisibleCount = 0;
            for (var i = 0; i < _items.Count; i++)
            {
                var on = i < stock.Count && stock[i].def.StackSprite != null;
                _items[i].gameObject.SetActive(on);
                if (!on) continue;
                VisibleCount++;
                // Astra's display view for this facing (A on left-facing pieces), fitted by its visible bounds; else the stack sprite.
                var view = stock[i].def.DisplaySprite(Facing == Facing.B, out var visible);
                if (view != null)
                {
                    _items[i].sprite = view;
                    FitVisible(_items[i], i, visible);
                }
                else
                {
                    _items[i].sprite = stock[i].def.StackSprite;
                    Fit(_items[i], i);
                }
                _badges[i].text = stock[i].count > 1 ? "×" + stock[i].count : "";
            }
        }

        /// <summary>Scales a display view uniformly so its visible part fits the box, standing on the box's bottom centre.</summary>
        private void FitVisible(SpriteRenderer sr, int i, Rect visiblePx)
        {
            var box = _boxes[i];
            var ppu = sr.sprite.pixelsPerUnit;
            var w = Mathf.Max(visiblePx.width, 1f) / ppu;
            var h = Mathf.Max(visiblePx.height, 1f) / ppu;
            var scale = Mathf.Min(box.width / w, box.height / h);
            sr.transform.localScale = Vector3.one * scale;
            // The visible bottom centre, relative to the sprite's pivot (units, y up).
            var pivotPx = new Vector2(sr.sprite.pivot.x, sr.sprite.rect.height - sr.sprite.pivot.y);
            var foot = new Vector2((visiblePx.center.x - pivotPx.x) / ppu, (pivotPx.y - visiblePx.yMax) / ppu);
            sr.transform.localPosition = new Vector2(box.center.x, box.yMin) - foot * scale;
            var badge = _badges[i].transform;
            badge.localScale = Vector3.one / Mathf.Max(scale, 0.001f);
            badge.position = transform.TransformPoint(new Vector3(box.xMax, box.yMin + 0.04f, 0f));
        }

        private void Fit(SpriteRenderer sr, int i)
        {
            var box = _boxes[i];
            var b = sr.sprite.bounds;   // local to the sprite's pivot
            var scale = Mathf.Min(box.width / Mathf.Max(b.size.x, 0.001f), box.height / Mathf.Max(b.size.y, 0.001f));
            sr.transform.localScale = Vector3.one * scale;
            var bottom = _def.Display == FurnitureDisplay.Potion;
            var target = bottom ? new Vector2(box.center.x, box.yMin) : box.center;
            var anchor = bottom ? new Vector2(b.center.x, b.min.y) : (Vector2)b.center;
            sr.transform.localPosition = target - anchor * scale;
            var badge = _badges[i].transform;
            badge.localScale = Vector3.one / Mathf.Max(scale, 0.001f);
            badge.position = transform.TransformPoint(new Vector3(box.xMax, box.yMin + 0.04f, 0f));
        }
    }
}
