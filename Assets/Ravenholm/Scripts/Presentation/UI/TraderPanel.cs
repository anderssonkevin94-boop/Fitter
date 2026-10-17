using System;
using System.Collections.Generic;
using System.Text;
using Ravenholm.Domain.Jobs;
using Ravenholm.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The periodic Trader (GDD 16): this visit's stock with exact prices and quantities, and selling
    /// unreserved Store goods at the Trader's poor prices. When away, shows when it returns.
    /// </summary>
    public sealed class TraderPanel : MonoBehaviour
    {
        private UiFactory _ui;
        private UiTheme _theme;
        private GameRoot _root;
        private RectTransform _content;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _status;
        private readonly List<(Button button, TextMeshProUGUI label, Func<string> text, Func<bool> enabled)> _live =
            new List<(Button, TextMeshProUGUI, Func<string>, Func<bool>)>();
        private string _structureKey = "";
        private float _nextRefresh;

        public bool IsOpen => gameObject.activeSelf;
        public string LastMessage { get; private set; } = "";

        public static TraderPanel Create(Transform parent, UiFactory ui, GameRoot root)
        {
            var content = ui.ModalScroll(parent, "TraderLayer", out var layer, out var title);
            var panel = layer.AddComponent<TraderPanel>();
            panel._ui = ui;
            panel._theme = ui.Theme;
            panel._root = root;
            panel._content = content;
            panel._title = title;
            layer.SetActive(false);
            return panel;
        }

        private EconomyService Eco => _root.Services.Economy;

        public void Show()
        {
            LastMessage = "";
            _structureKey = "";
            if (!IsOpen)
            {
                gameObject.SetActive(true);
                TouchInputRouter.PushModal();
            }
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
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        public bool Buy(string entryKey)
        {
            var ok = Eco.Buy(entryKey, out var failure);
            LastMessage = ok ? "Bought." : failure;
            if (ok) FeedbackAudio.Play(FeedbackAudio.Cue.Coin);
            _structureKey = "";
            Refresh();
            return ok;
        }

        public bool Sell(string instanceId)
        {
            var name = UnitVisuals.DisplayName(_root.Services.Logistics.System.DefinitionOfItem(instanceId));
            var ok = Eco.Sell(instanceId, out var failure);
            LastMessage = ok ? $"Sold {name}." : failure;
            _structureKey = "";
            Refresh();
            return ok;
        }

        private void Refresh()
        {
            var key = StructureKey();
            if (key != _structureKey)
            {
                _structureKey = key;
                Rebuild();
            }
            if (_status != null) _status.text = StatusText();
            foreach (var b in _live)
            {
                b.label.text = b.text();
                b.button.interactable = b.enabled == null || b.enabled();
            }
        }

        private string StructureKey()
        {
            var t = Eco.Trader;
            var sb = new StringBuilder();
            sb.Append(t != null && t.IsPresent(Eco.Now)).Append('|').Append(t != null ? t.VisitIndexAt(Eco.Now) : -2);
            foreach (var id in _root.Services.Logistics.System.StoreItems) sb.Append('|').Append(id);
            return sb.ToString();
        }

        private string StatusText()
        {
            var sb = new StringBuilder();
            sb.Append(Eco.DescribeTrader()).Append(".\nCompany gold: <b>").Append(_root.Services.Jobs.State.Gold).Append("</b>");
            if (!string.IsNullOrEmpty(LastMessage)) sb.Append("\n<b>").Append(UnitVisuals.PrettyIds(LastMessage)).Append("</b>");
            return sb.ToString();
        }

        private void Rebuild()
        {
            WorkshopUi.Clear(_content);
            _live.Clear();
            _title.text = "Wandering Trader";
            _status = _ui.LiveText(_content, _theme.smallSize);
            var t = Eco.Trader;
            var present = t != null && t.IsPresent(Eco.Now);
            if (present)
            {
                Header("For sale this visit");
                foreach (var o in t.Offers(Eco.Now))
                {
                    var key = o.Entry.Key;
                    var goods = o.GoodsId;
                    var kind = o.Entry.Goods;
                    TradeRow(GoodsIcon(goods), () => OfferText(key, goods, kind), () => Buy(key), () => OfferEnabled(key), "Buy");
                }

                Header("Sell from the Store (poor prices)");
                var log = _root.Services.Logistics.System;
                var any = false;
                foreach (var id in new List<string>(log.StoreItems))
                {
                    if (log.IsReserved(id)) continue;   // reserved for a loadout: never sold out from under it
                    var instance = id;
                    any = true;
                    TradeRow(GoodsIcon(log.DefinitionOfItem(instance)), () => $"{UnitVisuals.DisplayName(log.DefinitionOfItem(instance))}\nTrader pays {SellValue(instance)} gold", () => Sell(instance), null, "Sell");
                }
                if (!any) _ui.LiveText(_content, _theme.smallSize).text = "Nothing in the Store to sell. Gear on expeditions or reserved for a loadout cannot be sold.";
            }
            else
            {
                _ui.LiveText(_content, _theme.smallSize).text =
                    "The Trader visits for a few minutes at a time. Iron ingots and cedar lumber are always on offer, so you can rebuild after a bad run. Rope, fur, herbs and rarer goods vary by visit.";
            }
            _ui.FooterButton(_content, "Close", "Close", Close);
        }

        private Sprite GoodsIcon(string id)
        {
            var cat = _root.Services.Catalogue;
            if (cat.TryGet<Content.ItemDefinition>(id, out var item)) return WorkshopUi.ItemSprite(item);
            if (cat.TryGet<Content.ResourceDefinition>(id, out var resource)) return resource.Icon;
            foreach (var recipe in cat.Recipes)
                if (recipe != null && recipe.RequiredBlueprint != null && recipe.RequiredBlueprint.Id == id)
                    return WorkshopUi.ItemSprite(recipe.OutputItem);
            return _theme.board;
        }

        private void TradeRow(Sprite sprite, Func<string> text, Action action, Func<bool> enabled, string verb)
        {
            var card = _ui.Panel("Trade", _content, _theme.chip);
            card.raycastTarget = false;
            card.gameObject.AddComponent<LayoutElement>().preferredHeight = 224;
            var icon = _ui.Icon("Goods", card.transform, sprite, 144);
            UiFactory.Place(icon.rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(24, 0), new Vector2(144, 144));
            var label = _ui.Text("Description", card.transform, text(), 32, _theme.ink);
            UiFactory.Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(192, 24);
            label.rectTransform.offsetMax = new Vector2(-214, -24);
            SandboxHud.AutoFit(label, 25);
            var button = _ui.Button(verb, card.transform, verb, action, out _);
            UiFactory.Place((RectTransform)button.transform, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-24, 0), new Vector2(166, 132));
            _live.Add((button, label, text, enabled));
        }

        private int SellValue(string instanceId)
        {
            var def = _root.Services.Logistics.System.DefinitionOfItem(instanceId);
            return def != null && _root.Services.Catalogue.TryGet<Content.ItemDefinition>(def, out var item) ? item.TraderBuyValue : 0;
        }

        private TraderOffer Current(string key) => Eco.Trader.Offers(Eco.Now).Find(o => o.Entry.Key == key);

        private string OfferText(string key, string goods, TraderGoods kind)
        {
            var o = Current(key);
            if (o == null) return "Gone";
            var name = kind == TraderGoods.Blueprint ? $"Blueprint: {UnitVisuals.DisplayName(goods)}" : UnitVisuals.DisplayName(goods);
            return o.Remaining <= 0 ? $"{name} · sold out this visit" : $"<b>{name}</b>\n{o.Entry.Price} gold · {o.Remaining} left";
        }

        private bool OfferEnabled(string key)
        {
            var o = Current(key);
            return o != null && o.Remaining > 0 && _root.Services.Jobs.State.Gold >= o.Entry.Price;
        }

        private void Header(string text)
        {
            var h = _ui.Text("Header", _content, text, 36, WorkshopUi.Iron, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            h.gameObject.AddComponent<LayoutElement>().preferredHeight = 88f;
            SandboxHud.AutoFit(h, 28);
        }

        private void Row(Func<string> text, Action onClick, Func<bool> enabled)
        {
            var b = _ui.RowButton(_content, onClick, out var label);
            _live.Add((b, label, text, enabled));
        }
    }
}
