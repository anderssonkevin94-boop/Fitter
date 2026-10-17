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
            layer.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.07f, 0.05f, 0.18f);
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
            WorkshopUi.Bounds(card.rectTransform, .025f, .055f, .975f, .94f);
            var title = _ui.Text("Title", card.transform, "The Store", _theme.titleSize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UiFactory.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(48f, -36f), new Vector2(940f, 80f));

            // Tab row (two lines of three so every target stays thumb-sized).
            var tabs = UiFactory.Rect("Tabs", card.transform);
            tabs.anchorMin = new Vector2(0, 1);
            tabs.anchorMax = new Vector2(1, 1);
            tabs.pivot = new Vector2(0.5f, 1);
            tabs.sizeDelta = new Vector2(-112f, 284f);
            tabs.anchoredPosition = new Vector2(0, -126f);
            var grid = tabs.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(320f, 132f);
            grid.spacing = new Vector2(12f, 12f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            foreach (Tab t in System.Enum.GetValues(typeof(Tab)))
            {
                var tab = t;
                var b = _ui.Button(t.ToString(), tabs, t.ToString(), () => Select(tab), out var label);
                Destroy(b.GetComponent<LayoutElement>());
                label.fontSize = _theme.smallSize;
                _tabButtons[t] = b.transform.Find("Face").GetComponent<Image>();
            }

            var viewport = UiFactory.Rect("Viewport", card.transform);
            UiFactory.Stretch(viewport);
            viewport.offsetMin = new Vector2(44f, 240f);
            viewport.offsetMax = new Vector2(-44f, -434f);
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

            var footer = UiFactory.Rect("Footer", card.transform);
            WorkshopUi.Bounds(footer, 0, 0, 1, 0);
            footer.offsetMin = new Vector2(56, 48); footer.offsetMax = new Vector2(-56, 180);
            var row = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 18; row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;
            _ui.Button("JobBoard", footer, "Job Board", () => _root.Hud.ShowJobBoard(), out _);
            _ui.Button("Close", footer, "Close", Close, out _);
            _ui.AddScrollHint(card.transform, scroll, 188);
            tabs.gameObject.AddComponent<WorkshopGrid>().Columns = 3;

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
            foreach (var kv in _tabButtons) kv.Value.color = kv.Key == _tab ? WorkshopUi.Gold : WorkshopUi.Cream;
            var rows = BuildRows();
            var key = _tab + "|" + string.Join("|", rows.ConvertAll(r => r.text));
            if (key == _key) return;
            _key = key;
            WorkshopUi.Clear(_list);
            foreach (var (icon, text, pin) in rows) Row(icon, text, pin);
        }

        /// <summary>Pins an item type to the racks (also used by tests).</summary>
        public bool TogglePin(string itemDefinitionId)
        {
            var on = _root.Services.Logistics.TogglePin(itemDefinitionId);
            _key = "";
            Refresh();
            return on;
        }

        private List<(Sprite icon, string text, string pin)> BuildRows()
        {
            var rows = new List<(Sprite, string, string)>();
            var cat = _root.Services.Catalogue;
            var st = _root.Services.Logistics.System.State;
            switch (_tab)
            {
                case Tab.Materials:
                    foreach (var kv in st.SharedMaterials)
                        if (cat.TryGet<ResourceDefinition>(kv.Key, out var r))
                            rows.Add((r.Icon, $"<b>{r.DisplayName}</b> ×{kv.Value}\n<size=80%>{r.Category} · shared inventory, usable at any eligible station</size>", null));
                    if (rows.Count == 0) rows.Add((null, "No delivered materials yet.", null));
                    break;

                case Tab.Blueprints:
                    var prod = _root.Services.Production.System;
                    foreach (var rec in cat.Recipes)
                    {
                        if (rec == null || rec.RequiredBlueprint == null || !prod.State.Blueprints.Contains(rec.RequiredBlueprint.Id)) continue;
                        var costs = new StringBuilder();
                        foreach (var c in rec.Cost) costs.Append(costs.Length > 0 ? ", " : "").Append(c.quantity).Append(' ').Append(c.resource.DisplayName);
                        rows.Add((WorkshopUi.ItemSprite(rec.OutputItem),
                            $"<b>{rec.OutputItem.DisplayName}</b> · {rec.OutputItem.StatLine()}\n<size=80%>Craft at {rec.Station.DisplayName}: {costs} · permanent</size>", null));
                    }
                    if (rows.Count == 0) rows.Add((null, "No blueprints yet. Complete expeditions to discover new recipes.", null));
                    break;

                default:
                    var counts = new SortedDictionary<string, int>();
                    foreach (var inst in st.StoreItems)
                        if (st.ItemDefinitions.TryGetValue(inst, out var def)) counts[def] = counts.TryGetValue(def, out var n) ? n + 1 : 1;
                    foreach (var kv in counts)
                    {
                        if (!cat.TryGet<ItemDefinition>(kv.Key, out var item) || !Matches(item)) continue;
                        var pinned = st.PinnedDefinitions.Contains(item.Id);
                        rows.Add((WorkshopUi.ItemSprite(item),
                            $"<b>{item.DisplayName}</b> ×{kv.Value}{(pinned ? " · <b>pinned to the racks</b>" : "")}\n<size=80%>{item.StatLine()}{Compatibility(item)} · In Store · Trader pays {item.TraderBuyValue} g</size>",
                            item.Id));
                    }
                    if (rows.Count == 0) rows.Add((null, "Nothing here yet. Craft at a station, then carry it to the Store's receiving strip.", null));
                    break;
            }
            return rows;
        }

        private bool Matches(ItemDefinition item)
        {
            switch (_tab)
            {
                case Tab.Weapons: return item.Category == ItemCategory.Weapon;
                case Tab.Armor: return item.IsArmourLike;
                case Tab.Potions: return item.IsConsumable;
                default: return true;
            }
        }

        /// <summary>Phase 4 replaces this with class data; until then the weapon tag is the compatibility.</summary>
        private static string Compatibility(ItemDefinition item) =>
            item.Category == ItemCategory.Weapon ? "" : item.IsArmourLike ? " · any class" : " · used only if triggered";

        private void Row(Sprite icon, string text, string pinId)
        {
            var row = _ui.Panel("Row", _list, _theme.chip);
            row.raycastTarget = false;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 240f;
            if (icon != null)
            {
                var i = _ui.Icon("Icon", row.transform, icon, 144f);
                UiFactory.Place(i.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(24f, 0), new Vector2(144f, 144f));
            }
            var t = _ui.Text("Text", row.transform, text, _theme.smallSize - 2f, _theme.ink, TextAlignmentOptions.MidlineLeft);
            UiFactory.Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(icon != null ? 192f : 28f, 8f);
            t.rectTransform.offsetMax = new Vector2(pinId != null ? -200f : -24f, -16f);
            SandboxHud.AutoFit(t, 25f);
            if (pinId != null)
            {
                var pinned = _root.Services.Logistics.System.State.PinnedDefinitions.Contains(pinId);
                var b = _ui.Button("Pin", row.transform, pinned ? "Unpin" : "Pin", () => TogglePin(pinId), out var label, 132f);
                label.fontSize = _theme.smallSize - 4f;
                UiFactory.Place((RectTransform)b.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16f, 0), new Vector2(170f, 132f));
            }
        }
    }
}
