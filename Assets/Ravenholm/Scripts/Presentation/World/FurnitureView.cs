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
        private ShopFurnitureDefinition.Art _art;
        private SpriteRenderer _armorLayer, _headLayer, _headwearLayer, _shieldLayer;

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
            _art = art;
            _visual.sprite = art.sprite;
            ShowEquipment(null, null, null);
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
            // The armour stand dressed in layers holds a set (Kevin, 2026-10-16): body armour, a helmet and a shield, each
            // with its own count; the three share the stand's box.
            var rects = LayeredStand && art.slots.Count > 0 ? new List<Rect> { art.slots[0], art.slots[0], art.slots[0] } : art.slots;
            foreach (var s in rects)
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
        /// <summary>What it shows now, spot by spot (a null def is an empty spot); the display picker reads it.</summary>
        public IReadOnlyList<(ItemDefinition def, int count)> CurrentStock { get; private set; } = System.Array.Empty<(ItemDefinition, int)>();

        /// <summary>Changes whenever what it shows changes.</summary>
        public string StockKey => _stockKey;

        public void SetStock(IReadOnlyList<(ItemDefinition def, int count)> stock)
        {
            CurrentStock = stock;
            var key = new System.Text.StringBuilder();
            foreach (var s in stock) key.Append(s.def != null ? s.def.Id : "-").Append('x').Append(s.count).Append(',');   // a gap: an empty spot
            if (key.ToString() == _stockKey) return;
            _stockKey = key.ToString();
            VisibleCount = 0;
            ShowEquipment(null, null, null);
            if (LayeredStand)
            {
                StockSet(stock);
                return;
            }
            for (var i = 0; i < _items.Count; i++)
            {
                var on = i < stock.Count && stock[i].def != null && stock[i].def.StackSprite != null;
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

        /// <summary>
        /// Whether this piece shows equipment as layers on its own canvas (the armour stand in facing A, from Astra's
        /// handoff, 2026-10-16): only facing A was supplied, so facing B keeps its fitted views.
        /// </summary>
        public bool LayeredStand => _def != null && _def.Display == FurnitureDisplay.Armor && Facing == Facing.A && !Mirrored && _art?.armored != null;

        /// <summary>The equipment layers drawn now (tests): body armour, its collar head, headwear, shield.</summary>
        public (Sprite armor, Sprite head, Sprite headwear, Sprite shield) EquipmentShown =>
            (Shown(_armorLayer), Shown(_headLayer), Shown(_headwearLayer), Shown(_shieldLayer));

        private static Sprite Shown(SpriteRenderer sr) => sr != null && sr.enabled ? sr.sprite : null;

        /// <summary>
        /// Dresses the armour stand. Every layer shares the stand's 512 x 512 canvas and ground pivot, so each is drawn where
        /// the stand is, unscaled. Back to front (Astra's README): the support alone (under body armour; the bare
        /// neckless mannequin otherwise), the armour, the head seated in its collar, headwear, a shield beside it. A
        /// stand holds one item in the shop today; all three together are supported for when a stand shows a set.
        /// </summary>
        public void ShowEquipment(ItemDefinition armor, ItemDefinition headwear, ItemDefinition shield)
        {
            if (!LayeredStand) armor = headwear = shield = null;
            if (_art != null) _visual.sprite = armor != null && _art.armored != null ? _art.armored : _art.sprite;
            Layer(ref _armorLayer, "Armour", 1, armor != null ? armor.DisplaySprite(false, out _) : null);
            Layer(ref _headLayer, "Head", 2, armor != null ? armor.StandHeadA : null);
            Layer(ref _headwearLayer, "Headwear", 3, headwear != null ? headwear.DisplaySprite(false, out _) : null);
            Layer(ref _shieldLayer, "Shield", 4, shield != null ? shield.DisplaySprite(false, out _) : null);
        }

        private void Layer(ref SpriteRenderer sr, string name, int order, Sprite sprite)
        {
            if (sr == null)
            {
                if (sprite == null) return;
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);   // the stand's ground point: the layers' shared pivot
                sr = go.AddComponent<SpriteRenderer>();
                sr.spriteSortPoint = SpriteSortPoint.Pivot;
                sr.sharedMaterial = _visual.sharedMaterial;
                sr.sortingOrder = order;   // the stand itself is 0, the count badge 6
            }
            sr.sprite = sprite;
            sr.enabled = sprite != null;
        }

        /// <summary>Where each count badge stands on the dressed stand (local units from its ground point): by the chest, the head, the shield.</summary>
        private static readonly Vector2[] SetBadges = { new Vector2(0.36f, 0.55f), new Vector2(0.22f, 1.18f), new Vector2(-0.5f, 0.1f) };

        /// <summary>
        /// The dressed stand's stock: the first body armour, helmet and shield in the list (slots 0, 1, 2). Body armour
        /// without a stand layer yet (the Padded Gambeson, the Leather Jerkin) keeps its fitted view in the box.
        /// </summary>
        private void StockSet(IReadOnlyList<(ItemDefinition def, int count)> stock)
        {
            var set = new (ItemDefinition def, int count)[3];
            foreach (var s in stock)
            {
                if (s.def == null || s.def.StackSprite == null) continue;
                var at = s.def.Category == ItemCategory.Helmet ? 1 : s.def.Category == ItemCategory.Shield ? 2 : 0;
                if (set[at].def == null) set[at] = s;
            }
            var armor = set[0].def;
            var layered = armor != null && armor.StandLayer == StandLayer.Armor;
            ShowEquipment(layered ? armor : null, set[1].def?.StandLayer == StandLayer.Head ? set[1].def : null,
                set[2].def?.StandLayer == StandLayer.Shield ? set[2].def : null);
            for (var i = 0; i < _items.Count; i++)
            {
                var on = i < set.Length && set[i].def != null;
                _items[i].gameObject.SetActive(on);
                if (!on) continue;
                VisibleCount++;
                if (i == 0 && !layered)
                {
                    // Not drawn for the stand yet: its own view (or pile) in the box, as before.
                    var view = armor.DisplaySprite(false, out var visible);
                    _items[i].sprite = view != null ? view : armor.StackSprite;
                    if (view != null) FitVisible(_items[i], i, visible); else Fit(_items[i], i);
                }
                else
                {
                    _items[i].sprite = null;
                    _items[i].transform.localScale = Vector3.one;
                    _items[i].transform.localPosition = Vector3.zero;
                    _badges[i].transform.localScale = Vector3.one;
                    _badges[i].transform.localPosition = SetBadges[i];
                }
                _badges[i].text = set[i].count > 1 ? "×" + set[i].count : "";
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
