using System;
using System.Collections.Generic;
using System.Text;
using Ravenholm.Content;
using Ravenholm.Domain.Shop;
using Ravenholm.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The shop's build mode controls (Shop Titans style): a card along the bottom, leaving the shop visible and tappable
    /// above it. Pick a piece in the world to move (tap the floor), turn, put away or upgrade it; open Furniture to place
    /// stored pieces or buy new ones; pick a plot to buy it. Commands go to the shop service; the panel never edits state.
    /// </summary>
    public sealed class ShopBuildPanel : MonoBehaviour
    {
        private UiFactory _ui;
        private UiTheme _theme;
        private GameRoot _root;
        private TextMeshProUGUI _title, _info, _message;
        private RectTransform _buttons;
        private readonly List<(TextMeshProUGUI label, Button button, Func<string> text, Func<bool> enabled)> _live =
            new List<(TextMeshProUGUI, Button, Func<string>, Func<bool>)>();
        private bool _listing;
        private bool _listingKits;
        private string _key = "";

        public bool IsOpen => gameObject.activeSelf;
        public string InfoText => _info != null ? _info.text : "";
        public string Message => _message != null ? _message.text : "";

        public static ShopBuildPanel Create(Transform parent, UiFactory ui, GameRoot root)
        {
            var card = ui.Panel("ShopBuild", parent, ui.Theme.panel);
            UiFactory.Place(card.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 24f), new Vector2(1060f, 850f));
            var panel = card.gameObject.AddComponent<ShopBuildPanel>();
            panel.Build(ui, root);
            card.gameObject.SetActive(false);
            return panel;
        }

        private void Build(UiFactory ui, GameRoot root)
        {
            _ui = ui;
            _theme = ui.Theme;
            _root = root;
            var content = UiFactory.Rect("Content", transform);
            UiFactory.Stretch(content, 48f);
            UiFactory.Column(content.gameObject, 10f, new RectOffset(8, 8, 4, 4));
            _title = _ui.Text("Title", content, "", _theme.bodySize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _title.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
            _info = _ui.Text("Info", content, "", _theme.smallSize, _theme.ink, TextAlignmentOptions.TopLeft);
            _info.richText = true;
            _info.gameObject.AddComponent<LayoutElement>().preferredHeight = 185f;
            _message = _ui.Text("Message", content, "", _theme.smallSize - 2f, new Color32(180, 64, 52, 255), TextAlignmentOptions.TopLeft, FontStyles.Bold);
            _message.gameObject.AddComponent<LayoutElement>().preferredHeight = 84f;
            SandboxHud.AutoFit(_title, 28f);
            SandboxHud.AutoFit(_info, 26f);
            SandboxHud.AutoFit(_message, 25f);
            var viewport = UiFactory.Rect("Catalogue", content);
            viewport.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            _buttons = UiFactory.Rect("Buttons", viewport);
            _buttons.anchorMin = new Vector2(0, 1); _buttons.anchorMax = Vector2.one;
            _buttons.pivot = new Vector2(.5f, 1); _buttons.sizeDelta = Vector2.zero;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = _buttons; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            _buttons.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _buttons.gameObject.AddComponent<WorkshopGrid>();
            var grid = _buttons.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(470f, 132f);
            grid.spacing = new Vector2(16f, 12f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            var hint = _ui.Text("MoreBelow", content, "Scroll for more furniture", 26, WorkshopUi.Iron, TextAlignmentOptions.Center);
            hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 36;
            var watch = hint.gameObject.AddComponent<WorkshopScrollHint>();
            watch.Scroll = scroll; watch.Label = hint;
            var done = _ui.Button("CloseBuild", content, "Finish building", () => _root.Hud.CloseShopBuild(), out _);
            done.transform.SetAsLastSibling();
        }

        private static ShopView View => ShopView.Current;
        private ShopService Shop => _root.Services.Shop;

        public void Open()
        {
            if (View == null) return;
            gameObject.SetActive(true);
            _listing = false;
            _listingKits = false;
            View.SetBuildMode(true);
            View.SelectionChanged += Rebuild;
            StationBuild.SelectionChanged += Rebuild;
            Rebuild();
        }

        public void Close()
        {
            if (!IsOpen) return;
            gameObject.SetActive(false);
            StationBuild.SelectionChanged -= Rebuild;
            StationBuild.Deselect();
            if (View == null) return;
            View.SelectionChanged -= Rebuild;
            View.SetBuildMode(false);
        }

        private void Update()
        {
            if (View == null) { Close(); return; }
            foreach (var b in _live)
            {
                b.label.text = b.text();
                b.button.interactable = b.enabled == null || b.enabled();
            }
            _message.gameObject.SetActive(!string.IsNullOrEmpty(_message.text));
            if (Key() != _key) Rebuild();
        }

        private bool Placed(string id) => id != null && Shop.Find(id) is Placement p && p.Placed;

        private StationsService Stations => _root.Services.Stations;

        private int KitCount()
        {
            var n = 0;
            foreach (var _ in Stations.Kits()) n++;
            return n;
        }

        private string Key() =>
            $"{Shop.State.Furniture.Count}|{Shop.State.Expansions}|{Shop.WagonLevel}|{View.Selected}|{View.SelectedPlot}|{_listing}|{_listingKits}|{Placed(View.Selected)}" +
            $"|{StationBuild.Selected}|{KitCount()}|{(StationBuild.Selected != null && Stations.Site(StationBuild.Selected) is Ravenholm.Domain.Camp.StationSite s && s.Standing)}";

        // ------------------------------------------------------------------ states

        private void Rebuild()
        {
            if (View == null) return;
            _key = Key();
            WorkshopUi.Clear(_buttons);
            _live.Clear();
            _message.text = StationBuild.Selected != null || _listingKits ? StationBuild.LastMessage : View.LastMessage;
            _message.gameObject.SetActive(!string.IsNullOrEmpty(_message.text));
            _info.GetComponent<LayoutElement>().preferredHeight = _listing || _listingKits ? 90 : 185;

            if (StationBuild.Selected != null) { StationChoice(StationBuild.Selected); return; }
            if (_listingKits) { ListKits(); return; }
            if (_listing) { ListFurniture(); return; }
            if (View.SelectedPlot.HasValue) { PlotChoice(View.SelectedPlot.Value); return; }
            var p = View.Selected != null ? Shop.Find(View.Selected) : null;
            if (p != null) { PieceChoice(p); return; }

            var stored = Shop.State.Furniture.FindAll(x => !x.Placed).Count;
            _title.text = "Build mode";
            var kits = KitCount();
            var grow = Shop.Wagon ? WagonText() : "Tap a plot (marked with a sign) to grow the shop by 2 x 2 tiles.";
            _info.text = "Tap a piece to move it. " + grow + " Tap a station to set it up somewhere else." +
                         (stored > 0 ? $"\n{stored} piece{(stored == 1 ? "" : "s")} in storage." : "") +
                         (kits > 0 ? $"\n{kits} station{(kits == 1 ? "" : "s")} packed in kits." : "");
            Button(() => "Furniture", () => { _listing = true; Rebuild(); });
            if (Shop.Wagon)
                Button(() => Shop.WagonLevel >= Shop.Definition.WagonMaxLevel ? "Wagon fully upgraded" : $"Upgrade the wagon to {Shop.WagonLevel + 1}",
                    () => View.Report(Shop.UpgradeWagon() ?? "A bigger, finer carpet."), () => Shop.WagonUpgradeBlockedReason() == null);
            if (kits > 0) Button(() => $"Station kits ({KitCount()})", () => { _listingKits = true; Rebuild(); });
        }

        // ------------------------------------------------------------------ the camp's stations

        private string StationName(string id) => Stations.SetupName(id);   // a camp setup (the Blacksmith) by its own name

        private void StationChoice(string id)
        {
            var site = Stations.Site(id);
            _title.text = StationName(id);
            if (site == null) { Button(() => "Done", StationBuild.Deselect); return; }
            if (!site.Standing)
            {
                _info.text = $"Tap cleared, level ground where it should stand ({site.W} x {site.H} tiles). It takes a moment to build.";
                Button(() => "Cancel", StationBuild.Deselect);
                return;
            }
            _info.text = $"Tap cleared, level ground to set it up there ({site.W} x {site.H} tiles): the clearing, or forest ground with " +
                         "the trees and stumps cleared and the hills dug flat. It takes a moment to build.";
            Button(() => Stations.PackBlockedReason(id) ?? "Pack into its kit", () =>
            {
                var why = Stations.Pack(id);
                if (why == null) StationBuild.Deselect();
                else StationBuild.Report(why);
            }, () => Stations.PackBlockedReason(id) == null);
            Button(() => "Done", StationBuild.Deselect);
        }

        private void ListKits()
        {
            _title.text = "Station kits";
            _info.text = "Pick a station to set up, then tap where it should stand.";
            foreach (var s in Stations.Kits())
            {
                var id = s.StationId;
                Button(() => $"Set up {StationName(id)}", () => { _listingKits = false; StationBuild.Select(id); });
            }
            Button(() => "Back", () => { _listingKits = false; Rebuild(); });
        }

        private string WagonText()
        {
            var size = Shop.CarpetSize;
            if (Shop.WagonLevel >= Shop.Definition.WagonMaxLevel) return $"The wagon is fully upgraded ({size.x} x {size.y} carpet).";
            var next = Shop.Definition.CarpetSize(Shop.WagonLevel + 1);
            var c = Shop.NextWagonCost;
            var why = Shop.WagonUpgradeBlockedReason();
            return $"Wagon level {Shop.WagonLevel}, a {size.x} x {size.y} carpet. Level {Shop.WagonLevel + 1}: {next.x} x {next.y}, " +
                   $"{CostText(c.gold, c.materials)}." + (why != null ? $" <color=#B44034>{why}</color>" : "");
        }

        private void PieceChoice(Placement p)
        {
            var def = Shop.FurnitureOf(p);
            _title.text = $"{def.DisplayName} · level {p.Level}";
            if (!p.Placed)
            {
                _info.text = def.Wall ? "Tap a full wall to hang it." : "Tap the floor where it should stand.";
                Button(() => "Cancel", () => View.Deselect());
                return;
            }
            _info.text = (def.Wall ? "Tap another full wall to move it." : "Tap the floor to move it.") + "\n" + UpgradeText(p, def);
            if (!def.Wall) Button(() => "Turn", () => View.Report(Shop.Rotate(p.Id)));
            Button(() => "Put away", () => { var why = Shop.Store(p.Id); View.Deselect(); View.Report(why); });
            Button(() => p.Level >= def.MaxLevel ? "Fully upgraded" : $"Upgrade to {p.Level + 1}", () => View.Report(Shop.Upgrade(p.Id)),
                () => Shop.UpgradeBlockedReason(p) == null);
            Button(() => "Done", () => View.Deselect());
        }

        private string UpgradeText(Placement p, ShopFurnitureDefinition def)
        {
            if (p.Level >= def.MaxLevel) return "Fully upgraded.";
            var next = def.At(p.Level + 1);
            var why = Shop.UpgradeBlockedReason(p);
            return $"Level {p.Level + 1}: {CostText(next.gold, next.materials)}" + (why != null ? $"\n<color=#B44034>{why}</color>" : "");
        }

        private void PlotChoice(Tile block)
        {
            var c = Shop.NextBlockCost;
            _title.text = "Grow the shop";
            _info.text = $"Add this 2 x 2 plot to the shop.\nCost: {CostText(c.gold, c.materials)}";
            Button(() => Shop.BuyBlockedReason(block.A, block.B) ?? "Buy this plot", () =>
            {
                var why = Shop.BuyBlock(block.A, block.B);
                if (why == null) View.Deselect();
                View.Report(why ?? "The shop is bigger. Pieces whose wall moved are in storage.");
            }, () => Shop.BuyBlockedReason(block.A, block.B) == null);
            Button(() => "Cancel", () => View.Deselect());
        }

        private void ListFurniture()
        {
            _title.text = "Furniture";
            var sb = new StringBuilder("Place a stored piece, or buy a new one (it goes into storage).");
            _info.text = sb.ToString();
            foreach (var p in Shop.State.Furniture)
            {
                if (p.Placed) continue;
                var piece = p;
                var def = Shop.FurnitureOf(piece);
                Button(() => $"Place {def.DisplayName} ({piece.Level})", () => { _listing = false; View.Select(piece.Id); });
            }
            foreach (var def in _root.Services.Catalogue.ShopFurniture)
            {
                if (def == null || !def.ForSale) continue;
                var d = def;
                var first = d.At(1);
                Button(() => $"Buy {d.DisplayName}: {CostText(first.gold, first.materials)}", () =>
                {
                    var why = Shop.Buy(d, out var bought);
                    if (why == null) { _listing = false; View.Select(bought.Id); }
                    else View.Report(why);
                }, () => Shop.BuyBlockedReason(d) == null);
            }
            Button(() => "Back", () => { _listing = false; Rebuild(); });
        }

        private static string CostText(int gold, IReadOnlyList<ResourceCost> materials)
        {
            var sb = new StringBuilder().Append(gold).Append(" gold");
            foreach (var m in materials) sb.Append(", ").Append(m.quantity).Append(' ').Append(m.resource.DisplayName);
            return sb.ToString();
        }

        private void Button(Func<string> text, Action onClick, Func<bool> enabled = null)
        {
            var b = _ui.Button("Button", _buttons, text(), onClick, out var label);
            Destroy(b.GetComponent<LayoutElement>());
            label.fontSize = _theme.smallSize;
            label.enableAutoSizing = true;
            label.fontSizeMin = 18f;
            label.fontSizeMax = _theme.smallSize;
            _live.Add((label, b, text, enabled));
        }

        /// <summary>For tests: presses the button whose label starts with <paramref name="prefix"/>.</summary>
        public bool Press(string prefix)
        {
            foreach (var b in _live)
                if (b.label.text.StartsWith(prefix, StringComparison.Ordinal) && b.button.interactable) { b.button.onClick.Invoke(); return true; }
            return false;
        }
    }
}
