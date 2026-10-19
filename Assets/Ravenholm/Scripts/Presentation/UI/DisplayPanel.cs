using System;
using System.Collections.Generic;
using Ravenholm.Content;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Arrange a display piece (Kevin, 2026-10-18: "much like pressing on the heroes when giving them equipment"): its
    /// spots on top, the Store stock it can show in a swipe strip below, and Auto (the racks' own order: pinned,
    /// strongest, newest). Submits <c>ShopService.SetShown</c>; display only, inventory is untouched.
    /// </summary>
    public sealed class DisplayPanel : MonoBehaviour
    {
        private UiFactory _ui;
        private GameRoot _root;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _summary;
        private TextMeshProUGUI _message;
        private RectTransform _spots;
        private RectTransform _choices;
        private string _placementId;
        private int _spot;
        private string _key = "";
        private float _next;

        public bool IsOpen => gameObject.activeSelf;
        public string PlacementId => _placementId;
        public int SelectedSpot => _spot;

        public static DisplayPanel Create(Transform parent, UiFactory ui, GameRoot root)
        {
            var rect = UiFactory.Rect("DisplayLayer", parent);
            UiFactory.Stretch(rect);
            rect.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .08f);
            var panel = rect.gameObject.AddComponent<DisplayPanel>();
            panel._ui = ui; panel._root = root;
            panel.Build();
            rect.gameObject.SetActive(false);
            return panel;
        }

        private FurnitureView View => ShopView.Current != null && ShopView.Current.Views.TryGetValue(_placementId ?? "", out var v) ? v : null;

        /// <summary>Spots on this piece: a dressed armour stand has body, head and shield; the rest their boxes.</summary>
        public int SpotCount => View == null ? 0 : View.LayeredStand ? ShopView.LayeredSpots.Length : View.SlotCount;

        public void Show(string placementId)
        {
            _placementId = placementId;
            _spot = 0;
            _message.text = "";
            if (!IsOpen)
            {
                gameObject.SetActive(true);
                TouchInputRouter.PushModal();
            }
            _key = "";
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen) return;
            gameObject.SetActive(false);
            TouchInputRouter.PopModal();
        }

        private void OnDestroy()
        {
            if (IsOpen) TouchInputRouter.PopModal();
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            if (View == null) { Close(); return; }
            Refresh();
        }

        private void Build()
        {
            var card = WorkshopUi.Frame(transform);
            WorkshopUi.Bounds(card.rectTransform, .025f, .2f, .975f, .86f);
            _title = WorkshopUi.Label(_ui, card.transform, "DISPLAY", 52, .055f, .9f, .81f, .985f, true);
            var close = WorkshopUi.Button(_ui, "Close", card.transform, "×", Close, out _, false);
            WorkshopUi.Bounds((RectTransform)close.transform, .84f, .9f, .96f, .985f);
            _summary = WorkshopUi.Label(_ui, card.transform, "", 30, .055f, .83f, .945f, .9f);
            _spots = UiFactory.Rect("Spots", card.transform);
            WorkshopUi.Bounds(_spots, .05f, .5f, .95f, .83f);
            _choices = UiFactory.Rect("Choices", card.transform);
            WorkshopUi.Bounds(_choices, .05f, .16f, .95f, .49f);
            _message = WorkshopUi.Label(_ui, card.transform, "", 27, .055f, .11f, .945f, .16f, true, TextAlignmentOptions.Center);
            var done = WorkshopUi.Button(_ui, "Done", card.transform, "Done", Close, out _);
            WorkshopUi.Bounds((RectTransform)done.transform, .3f, .03f, .7f, .105f);
        }

        private IReadOnlyList<string> Chosen => _root.Services.Shop.Find(_placementId)?.Shown ?? (IReadOnlyList<string>)Array.Empty<string>();

        private string ChosenAt(int spot) => spot < Chosen.Count ? Chosen[spot] : "";

        private string SpotName(int spot) => View != null && View.LayeredStand
            ? ShopView.LayeredSpots[spot] switch { ItemCategory.Armor => "Body", ItemCategory.Helmet => "Head", _ => "Shield" }
            : "Spot " + (spot + 1);

        /// <summary>Stocked item types this spot can show (one per type, with its count), in the racks' order.</summary>
        private List<(ItemDefinition def, int count, string newest)> Options(int spot)
        {
            var v = View;
            if (v == null) return new List<(ItemDefinition, int, string)>();
            Func<ItemDefinition, bool> fits = v.LayeredStand ? (Func<ItemDefinition, bool>)(i => v.Definition.Shows(i) && i.Category == ShopView.LayeredSpots[spot]) : i => v.Definition.Shows(i) && v.Seats(i);
            return ShopView.Representative(_root, fits);
        }

        private ItemDefinition Item(string id) => !string.IsNullOrEmpty(id) && _root.Services.Catalogue.TryGet<ItemDefinition>(id, out var item) ? item : null;

        private void Refresh()
        {
            var v = View;
            if (v == null) return;
            var count = SpotCount;
            if (_spot >= count) _spot = 0;
            var options = Options(_spot);
            var key = _placementId + "|" + _spot + "|" + count + "|" + v.StockKey + "|" + string.Join(",", Chosen) + "|" + string.Join(",", options.ConvertAll(o => o.def.Id + "x" + o.count));
            if (key == _key) return;
            _key = key;
            _title.text = v.Definition.DisplayName.ToUpperInvariant();
            _summary.text = "Choose what each spot shows. <b>Auto</b> shows your best stock.";
            DrawSpots(count);
            DrawChoices(options);
        }

        private void DrawSpots(int count)
        {
            WorkshopUi.Clear(_spots);
            if (count == 0) { WorkshopUi.Label(_ui, _spots, "Nothing to arrange on this piece.", 30, 0, .4f, 1, .6f, false, TextAlignmentOptions.Center); return; }
            var cols = Mathf.Min(count, count > 4 ? (count + 1) / 2 : count);
            var rows = (count + cols - 1) / cols;
            var w = 1f / Mathf.Max(cols, 3);
            var x0 = (1f - w * cols) / 2f;
            for (var i = 0; i < count; i++)
            {
                var spot = i;
                var col = i % cols;
                var row = i / cols;
                var y1 = 1f - row / (float)rows;
                var y0 = 1f - (row + 1) / (float)rows;
                var chosen = Item(ChosenAt(i));
                var shown = Shown(i);
                var button = WorkshopUi.Button(_ui, "Spot" + i, _spots, "", () => SelectSpot(spot), out var label, false);
                WorkshopUi.Bounds((RectTransform)button.transform, x0 + col * w, y0, x0 + (col + 1) * w, y1, 6);
                button.transform.Find("Face").GetComponent<Image>().color = i == _spot ? new Color32(244, 211, 143, 255) : WorkshopUi.Cream;
                WorkshopUi.Label(_ui, button.transform, SpotName(i), 27, .02f, .76f, .98f, 1, true, TextAlignmentOptions.Center);
                WorkshopUi.Bounds(label.rectTransform, .015f, .01f, .985f, .28f);
                label.fontSizeMax = 25; label.fontSizeMin = 18;
                label.text = chosen != null ? chosen.DisplayName : shown != null ? "Auto · " + shown.DisplayName : "Auto";
                var art = chosen ?? shown;
                if (art != null)
                {
                    var icon = _ui.Icon("Item", button.transform, WorkshopUi.ItemSprite(art), 100);
                    WorkshopUi.Bounds(icon.rectTransform, .2f, .29f, .8f, .76f);
                    if (chosen == null) icon.color = new Color(1f, 1f, 1f, .55f);   // Auto: what the racks picked, dimmed
                }
            }
        }

        /// <summary>What the piece shows in a spot now (chosen or Auto), read from the rack's own stock.</summary>
        private ItemDefinition Shown(int spot)
        {
            var v = View;
            if (v == null) return null;
            var stock = v.CurrentStock;
            if (v.LayeredStand)
            {
                foreach (var s in stock) if (s.def != null && s.def.Category == ShopView.LayeredSpots[spot]) return s.def;
                return null;
            }
            return spot < stock.Count ? stock[spot].def : null;
        }

        private void DrawChoices(List<(ItemDefinition def, int count, string newest)> options)
        {
            WorkshopUi.Clear(_choices);
            WorkshopUi.Label(_ui, _choices, "CHOOSE FOR " + SpotName(_spot).ToUpperInvariant(), 33, 0, .8f, .65f, 1, true);
            WorkshopUi.Label(_ui, _choices, "Store stock · swipe →", 25, .65f, .8f, 1, 1);
            var view = UiFactory.Rect("ItemViewport", _choices);
            WorkshopUi.Bounds(view, 0, .12f, 1, .8f);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = Color.clear;
            var content = UiFactory.Rect("Items", view);
            content.anchorMin = Vector2.zero; content.anchorMax = Vector2.up; content.pivot = new Vector2(0, .5f);
            content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 14; layout.childControlHeight = true; layout.childForceExpandHeight = true;
            layout.childControlWidth = true; layout.childForceExpandWidth = false;
            content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content; scroll.viewport = view; scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var current = ChosenAt(_spot);
            AddChoice(content, null, 0, current == "");
            foreach (var o in options) AddChoice(content, o.def, o.count, o.def.Id == current);
            if (options.Count == 0)
                WorkshopUi.Label(_ui, _choices, "Nothing in the Store fits here yet.", 26, 0, 0, 1, .12f);
        }

        private void AddChoice(Transform content, ItemDefinition item, int count, bool chosen)
        {
            var id = item?.Id;
            var button = WorkshopUi.Button(_ui, "ChooseItem", content, "", () => Choose(id), out var label, chosen);
            var le = button.GetComponent<LayoutElement>(); le.minWidth = le.preferredWidth = 240; le.minHeight = 0;
            WorkshopUi.Bounds(label.rectTransform, 0, 0, 1, .32f);
            label.fontSizeMax = 26; label.fontSizeMin = 18;
            label.text = (chosen ? "• " : "") + (item != null ? item.DisplayName + (count > 1 ? $" ×{count}" : "") : "Auto");
            if (item != null)
            {
                var icon = _ui.Icon("Icon", button.transform, WorkshopUi.ItemSprite(item), 100);
                WorkshopUi.Bounds(icon.rectTransform, .2f, .34f, .8f, .97f);
            }
        }

        public void SelectSpot(int spot)
        {
            if (!IsOpen || spot < 0 || spot >= SpotCount) return;
            _spot = spot;
            _message.text = "";
            _key = "";
            Refresh();
        }

        /// <summary>Puts an item type (or Auto, null) in the selected spot. Returns whether it took.</summary>
        public bool Choose(string itemDefinitionId)
        {
            if (!IsOpen) return false;
            var why = _root.Services.Shop.SetShown(_placementId, _spot, itemDefinitionId);
            _message.text = why ?? "";
            _key = "";
            _next = 0;
            return why == null;
        }
    }
}
