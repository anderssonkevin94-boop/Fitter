using System.Collections.Generic;
using System.Text;
using Ravenholm.Content;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Store inventory (GDD 4): gear filters Weapon / Armor / Potion / All with item cards, plus
    /// separate Materials and Blueprints panels so the gear grid does not carry every economic
    /// concept. Every stored unit is listed even when the racks show only a subset.
    /// </summary>
    public sealed class StorePanel : MonoBehaviour
    {
        public enum Tab { Weapons, Armor, Potions, All, Materials, Blueprints }

        private UiFactory _ui;
        private UiTheme _theme;
        private GameRoot _root;
        private RectTransform _list;
        private readonly Dictionary<Tab, Image> _tabButtons = new Dictionary<Tab, Image>();
        private Tab _tab = Tab.All;
        private string _key = "";
        private float _nextRefresh;

        public bool IsOpen => gameObject.activeSelf;
        public Tab CurrentTab => _tab;
        public int RowCount => _list != null ? _list.childCount : 0;

        public static StorePanel Create(Transform parent, UiFactory ui, GameRoot root)
        {
            var layer = UiFactory.Rect("StoreLayer", parent);
            UiFactory.Stretch(layer);
            layer.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.07f, 0.05f, 0.4f);
            var panel = layer.gameObject.AddComponent<StorePanel>();
            panel.Build(ui, root);
            layer.gameObject.SetActive(false);
            return panel;
        }

        private void Build(UiFactory ui, GameRoot root)
        {
            _ui = ui;
            _theme = ui.Theme;
            _root = root;
            var card = _ui.Panel("Card", transform, _theme.panel);
            UiFactory.Stretch(card.rectTransform, 28f);
            var title = _ui.Text("Title", card.transform, "Store inventory", _theme.titleSize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UiFactory.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(48f, -36f), new Vector2(900f, 80f));

            // Tab row (two lines of three so every target stays thumb-sized).
            var tabs = UiFactory.Rect("Tabs", card.transform);
            tabs.anchorMin = new Vector2(0, 1);
            tabs.anchorMax = new Vector2(1, 1);
            tabs.pivot = new Vector2(0.5f, 1);
            tabs.sizeDelta = new Vector2(-72f, 280f);
            tabs.anchoredPosition = new Vector2(0, -126f);
            var grid = tabs.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(330f, 124f);
            grid.spacing = new Vector2(12f, 12f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            foreach (Tab t in System.Enum.GetValues(typeof(Tab)))
            {
                var tab = t;
                var b = _ui.Button(t.ToString(), tabs, t.ToString(), () => Select(tab), out var label);
                Destroy(b.GetComponent<LayoutElement>());
                label.fontSize = _theme.smallSize;
                _tabButtons[t] = b.GetComponent<Image>();
            }

            var viewport = UiFactory.Rect("Viewport", card.transform);
            UiFactory.Stretch(viewport);
            viewport.offsetMin = new Vector2(36f, 340f);
            viewport.offsetMax = new Vector2(-36f, -420f);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            _list = UiFactory.Rect("List", viewport);
            _list.anchorMin = new Vector2(0, 1);
            _list.anchorMax = new Vector2(1, 1);
            _list.pivot = new Vector2(0.5f, 1);
            _list.sizeDelta = Vector2.zero;
            UiFactory.Column(_list.gameObject, 12f, new RectOffset(8, 8, 8, 16));
            _list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = _list;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var boardBtn = _ui.Button("JobBoard", card.transform, "Job Board", () => _root.Hud.ShowJobBoard(), out _);
            var brt = (RectTransform)boardBtn.transform;
            brt.anchorMin = new Vector2(0, 0);
            brt.anchorMax = new Vector2(1, 0);
            brt.pivot = new Vector2(0.5f, 0);
            brt.sizeDelta = new Vector2(-72f, _theme.minTouchTarget);
            brt.anchoredPosition = new Vector2(0, 36f + _theme.minTouchTarget + 12f);

            var close = _ui.Button("Close", card.transform, "Close", Close, out _);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = new Vector2(0, 0);
            crt.anchorMax = new Vector2(1, 0);
            crt.pivot = new Vector2(0.5f, 0);
            crt.sizeDelta = new Vector2(-72f, _theme.minTouchTarget);
            crt.anchoredPosition = new Vector2(0, 36f);
        }

        public void Show()
        {
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

        public void Select(Tab tab)
        {
            _tab = tab;
            _key = "";
            Refresh();
        }

        private void OnDestroy()
        {
            if (IsOpen) TouchInputRouter.PopModal();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.5f;
            Refresh();
        }

        private void Refresh()
        {
            foreach (var kv in _tabButtons) kv.Value.color = kv.Key == _tab ? Color.white : new Color(0.82f, 0.78f, 0.7f);
            var rows = BuildRows();
            var key = _tab + "|" + string.Join("|", rows.ConvertAll(r => r.text));
            if (key == _key) return;
            _key = key;
            for (var i = _list.childCount - 1; i >= 0; i--) Destroy(_list.GetChild(i).gameObject);
            foreach (var (icon, text) in rows) Row(icon, text);
        }

        private List<(Sprite icon, string text)> BuildRows()
        {
            var rows = new List<(Sprite, string)>();
            var cat = _root.Services.Catalogue;
            var st = _root.Services.Logistics.System.State;
            switch (_tab)
            {
                case Tab.Materials:
                    foreach (var kv in st.SharedMaterials)
                        if (cat.TryGet<ResourceDefinition>(kv.Key, out var r))
                            rows.Add((r.Icon, $"<b>{r.DisplayName}</b> ×{kv.Value}\n<size=80%>{r.Category} · shared inventory, usable at any eligible station</size>"));
                    if (rows.Count == 0) rows.Add((null, "No delivered materials yet."));
                    break;

                case Tab.Blueprints:
                    var prod = _root.Services.Production.System;
                    foreach (var rec in cat.Recipes)
                    {
                        if (rec == null || rec.RequiredBlueprint == null || !prod.State.Blueprints.Contains(rec.RequiredBlueprint.Id)) continue;
                        var costs = new StringBuilder();
                        foreach (var c in rec.Cost) costs.Append(costs.Length > 0 ? ", " : "").Append(c.quantity).Append(' ').Append(c.resource.DisplayName);
                        rows.Add((rec.OutputItem.Icon,
                            $"<b>{rec.OutputItem.DisplayName}</b> · {rec.OutputItem.StatLine()}\n<size=80%>Craft at {rec.Station.DisplayName}: {costs} · permanent</size>"));
                    }
                    break;

                default:
                    var counts = new SortedDictionary<string, int>();
                    foreach (var inst in st.StoreItems)
                        if (st.ItemDefinitions.TryGetValue(inst, out var def)) counts[def] = counts.TryGetValue(def, out var n) ? n + 1 : 1;
                    foreach (var kv in counts)
                    {
                        if (!cat.TryGet<ItemDefinition>(kv.Key, out var item) || !Matches(item)) continue;
                        rows.Add((item.Icon,
                            $"<b>{item.DisplayName}</b> ×{kv.Value}\n<size=80%>{item.StatLine()}{Compatibility(item)} · In Store · Trader pays {item.TraderBuyValue} g</size>"));
                    }
                    if (rows.Count == 0) rows.Add((null, "Nothing here yet. Craft at a station, then carry it to the Store's receiving strip."));
                    break;
            }
            return rows;
        }

        private bool Matches(ItemDefinition item)
        {
            switch (_tab)
            {
                case Tab.Weapons: return item.Category == ItemCategory.Weapon;
                case Tab.Armor: return item.Category == ItemCategory.Armor;
                case Tab.Potions: return item.Category == ItemCategory.Potion;
                default: return true;
            }
        }

        /// <summary>Phase 4 replaces this with class data; until then the weapon tag is the compatibility.</summary>
        private static string Compatibility(ItemDefinition item) =>
            item.Category == ItemCategory.Weapon ? "" : item.Category == ItemCategory.Armor ? " · any class" : " · used only if triggered";

        private void Row(Sprite icon, string text)
        {
            var row = _ui.Panel("Row", _list, _theme.chip);
            row.raycastTarget = false;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 150f;
            if (icon != null)
            {
                var i = _ui.Icon("Icon", row.transform, icon, 104f);
                UiFactory.Place(i.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(24f, 0), new Vector2(104f, 104f));
            }
            var t = _ui.Text("Text", row.transform, text, _theme.smallSize - 2f, _theme.ink, TextAlignmentOptions.MidlineLeft);
            UiFactory.Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(icon != null ? 148f : 28f, 8f);
            t.rectTransform.offsetMax = new Vector2(-24f, -8f);
        }
    }
}
