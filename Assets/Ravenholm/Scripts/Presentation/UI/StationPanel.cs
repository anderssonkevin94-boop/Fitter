using System;
using System.Collections.Generic;
using System.Text;
using Ravenholm.Content;
using Ravenholm.Domain.Production;
using Ravenholm.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// One concise panel per production building (GDD 4/8/9/16): what it receives, its processing
    /// line (selected recipe, progress, missing input, reserve, queued change), and instant crafting
    /// for owned blueprints with exact costs and a clear reason when Craft is blocked.
    /// </summary>
    public sealed class StationPanel : MonoBehaviour
    {
        private sealed class LiveButton
        {
            public Button Button;
            public TextMeshProUGUI Label;
            public Func<string> Text;
            public Func<bool> Enabled;
        }

        private UiFactory _ui;
        private UiTheme _theme;
        private GameRoot _root;
        private StationInspectable _station;
        private RectTransform _content;
        private TextMeshProUGUI _title;
        private readonly List<LiveButton> _live = new List<LiveButton>();
        private readonly List<(TextMeshProUGUI text, Func<string> provider)> _liveTexts = new List<(TextMeshProUGUI, Func<string>)>();
        private string _structureKey = "";
        private float _nextRefresh;
        private string _lastMessage = "";

        public bool IsOpen => gameObject.activeSelf;
        public string StationId => _station != null ? _station.StationId : null;
        public string LastMessage => _lastMessage;

        public static StationPanel Create(Transform parent, UiFactory ui, GameRoot root)
        {
            var layer = UiFactory.Rect("StationLayer", parent);
            UiFactory.Stretch(layer);
            layer.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.07f, 0.05f, 0.4f);
            var panel = layer.gameObject.AddComponent<StationPanel>();
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
            _title = _ui.Text("Title", card.transform, "", _theme.titleSize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UiFactory.Place(_title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(48f, -36f), new Vector2(900f, 80f));

            var viewport = UiFactory.Rect("Viewport", card.transform);
            UiFactory.Stretch(viewport);
            viewport.offsetMin = new Vector2(36f, 36f);
            viewport.offsetMax = new Vector2(-36f, -130f);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            _content = UiFactory.Rect("Content", viewport);
            _content.anchorMin = new Vector2(0, 1);
            _content.anchorMax = new Vector2(1, 1);
            _content.pivot = new Vector2(0.5f, 1);
            _content.sizeDelta = Vector2.zero;
            UiFactory.Column(_content.gameObject, 16f, new RectOffset(12, 12, 8, 24));
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = _content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
        }

        public void Show(StationInspectable station)
        {
            _station = station;
            _lastMessage = "";
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
            _nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        private ProductionSystem Prod => _root.Services.Production.System;
        private ContentCatalogue Cat => _root.Services.Catalogue;

        /// <summary>Crafts a recipe through the service (used by the Craft buttons and by tests).</summary>
        public bool Craft(string recipeId)
        {
            var ok = _root.Services.Production.Craft(recipeId, out var failure);
            _lastMessage = ok ? $"Crafted {UnitVisuals.DisplayName(Prod.Recipe(recipeId).OutputItemId)}. It is on the output pad: carry it to the Store."
                              : UnitVisuals.PrettyIds(failure);
            if (ok) FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
            Refresh();
            return ok;
        }

        private void Refresh()
        {
            if (_station == null) return;
            var key = StructureKey();
            if (key != _structureKey)
            {
                _structureKey = key;
                Rebuild();
            }
            foreach (var t in _liveTexts) t.text.text = t.provider();
            foreach (var b in _live)
            {
                b.Label.text = b.Text();
                b.Button.interactable = b.Enabled == null || b.Enabled();
            }
        }

        private string StructureKey()
        {
            var sb = new StringBuilder(_station.StationId);
            sb.Append('|').Append(Prod.Level(_station.StationId));
            sb.Append('|').Append(_root.Services.Economy.Purchased.Count);
            foreach (var bp in Prod.State.Blueprints) sb.Append('|').Append(bp);
            return sb.ToString();
        }

        private void Rebuild()
        {
            for (var i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            _live.Clear();
            _liveTexts.Clear();
            var stationId = _station.StationId;
            var stationDef = Cat.TryGet<StationDefinition>(stationId, out var sd) ? sd : null;
            _title.text = _station.Title;

            Text(() => _station.BuildDetail(), _theme.smallSize);
            Text(() => string.IsNullOrEmpty(_lastMessage) ? "" : $"<b>{_lastMessage}</b>", _theme.smallSize);

            if (_station.HasReceiver)
            {
                Header("Receiving");
                Text(ReceiverText, _theme.smallSize);
            }

            var processes = new List<ProcessRecipe>();
            foreach (var p in Prod.Processes) if (p.Facility == stationId) processes.Add(p);
            if (processes.Count > 0)
            {
                Header("Processing line (automatic)");
                Text(ProcessText, _theme.smallSize);
                foreach (var p in processes)
                {
                    var proc = p;
                    Button(() => ProcessButtonText(proc), () => _root.Services.Production.SelectProcess(stationId, proc.Id),
                        () => Prod.Level(stationId) >= proc.MinLevel);
                }
                Button(ReserveText, CycleReserve, null);
            }

            var recipes = new List<CraftRecipe>();
            foreach (var r in Prod.Recipes)
                if (r.Station == stationId && Prod.State.Blueprints.Contains(r.BlueprintId)) recipes.Add(r);   // unknown recipes stay hidden (GDD 9)
            if (recipes.Count > 0)
            {
                Header("Craft (instant)");
                Text(() => stationDef != null ? $"Output pad: {_root.Services.Logistics.System.ContainerUnitCount(stationDef.OutputPadContainerId)}/{stationDef.OutputPadCapacity}" : "", _theme.smallSize);
                foreach (var r in recipes)
                {
                    var recipe = r;
                    Text(() => RecipeCard(recipe), _theme.smallSize);
                    Button(() => CraftButtonText(recipe), () => Craft(recipe.Id), () => Prod.CraftBlockedReason(recipe.Id) == null);
                }
            }

            if (stationDef != null) UpgradeSection(stationDef);

            if (stationDef != null)
                Button(() => $"DEBUG station level: {Prod.Level(stationId)} of {stationDef.MaxLevel} · tap to change",
                    () => _root.Services.Production.DebugCycleLevel(stationDef), null);
            Button(() => "Close", Close, null);
        }

        // ------------------------------------------------------------------ upgrades (GDD 16)

        private void UpgradeSection(StationDefinition station)
        {
            UpgradeDefinition next = null;
            foreach (var u in Cat.Upgrades)
                if (u != null && u.Kind == UpgradeKind.StationLevel && u.Station == station && u.Level == Prod.Level(station.Id) + 1) next = u;
            Header("Upgrade");
            if (next == null)
            {
                Text(() => Prod.Level(station.Id) >= station.MaxLevel ? "Fully upgraded." : "No further upgrade available yet.", _theme.smallSize);
                return;
            }
            var u2 = next;
            Text(() => UpgradeCard(_root, u2), _theme.smallSize);
            Button(() => UpgradeButtonText(_root, u2), () => BuyUpgrade(u2), () => _root.Services.Economy.UpgradeBlockedReason(u2) == null);
        }

        public bool BuyUpgrade(UpgradeDefinition u)
        {
            var ok = _root.Services.Economy.BuyUpgrade(u, out var failure);
            _lastMessage = ok ? $"{u.DisplayName} built. {u.Effect}." : failure;
            if (ok) FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
            _structureKey = "";
            Refresh();
            return ok;
        }

        /// <summary>Exact effect, cost against what you have, and prerequisite (shared with the site panel).</summary>
        public static string UpgradeCard(GameRoot root, UpgradeDefinition u)
        {
            var sb = new StringBuilder();
            sb.Append("<b>").Append(u.DisplayName).Append("</b>: ").Append(u.Effect);
            var gold = root.Services.Jobs.State.Gold;
            sb.Append("\nCost: ").Append($"<color={(gold >= u.GoldCost ? "#3B6E2E" : "#B44034")}>{u.GoldCost} gold (have {gold})</color>");
            foreach (var c in u.MaterialCost)
            {
                var have = root.Services.Logistics.System.SharedAvailable(c.resource.Id);
                sb.Append(" · ").Append($"<color={(have >= c.quantity ? "#3B6E2E" : "#B44034")}>{c.resource.DisplayName} {c.quantity} (have {have})</color>");
            }
            var prereq = root.Services.Economy.UpgradePrerequisiteReason(u);
            if (prereq != null) sb.Append("\n<color=#B44034>").Append(prereq).Append("</color>");
            return sb.ToString();
        }

        public static string UpgradeButtonText(GameRoot root, UpgradeDefinition u)
        {
            var reason = root.Services.Economy.UpgradeBlockedReason(u);
            return reason == null ? $"Build {u.DisplayName}" : reason;
        }

        // ------------------------------------------------------------------ row helpers

        private void Header(string text)
        {
            var t = _ui.Text("Header", _content, text, _theme.bodySize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
        }

        private void Text(Func<string> provider, float size)
        {
            var t = _ui.Text("Text", _content, "", size, _theme.ink, TextAlignmentOptions.TopLeft);
            t.richText = true;
            _liveTexts.Add((t, provider));
        }

        private void Button(Func<string> text, Action onClick, Func<bool> enabled)
        {
            _ui.Button("Button", _content, "", onClick, out var label);
            label.fontSize = _theme.smallSize;
            label.enableAutoSizing = true;
            label.fontSizeMin = 20f;
            label.fontSizeMax = _theme.smallSize;
            _live.Add(new LiveButton { Button = label.GetComponentInParent<Button>(), Label = label, Text = text, Enabled = enabled });
        }

        // ------------------------------------------------------------------ text

        private string ReceiverText()
        {
            var shared = _root.Services.Logistics.System.State.SharedMaterials;
            var accepts = new StringBuilder();
            var have = new StringBuilder();
            foreach (var r in Cat.Resources)
            {
                if (r == null || CatalogueAcceptanceRules.Map(r.AcceptedReceiver) != _station.ReceiverKind) continue;
                accepts.Append(accepts.Length > 0 ? ", " : "").Append(r.DisplayName);
                if (shared.TryGetValue(r.Id, out var q)) have.Append(have.Length > 0 ? ", " : "").Append(r.DisplayName).Append(" ×").Append(q);
            }
            return $"Stand on the marker to deliver. Accepts: {accepts}.\nDelivered raw stock (shared): {(have.Length > 0 ? have.ToString() : "none")}";
        }

        private string ProcessText()
        {
            var p = Prod.EnsureProcessor(_station.StationId);
            var sb = new StringBuilder();
            if (p.InFlight != null)
            {
                var r = Prod.Process(p.InFlight);
                var left = Mathf.Max(0, p.EndMs - Prod.State.SimMs) / 1000f;
                sb.Append($"Running: <b>{r.Name}</b> · {left:0.0} s left → +{r.OutputQuantity} {UnitVisuals.DisplayName(r.Output)}");
            }
            else
            {
                var reason = Prod.BlockedReason(_station.StationId);
                sb.Append(reason == null ? "Idle" : "Idle: " + UnitVisuals.PrettyIds(reason));
            }
            if (p.Queued != null) sb.Append($"\nNext after this batch: {Prod.Process(p.Queued).Name}");
            var shared = _root.Services.Logistics.System.State.SharedMaterials;
            var outputs = new SortedSet<string>();
            foreach (var r in Prod.Processes) if (r.Facility == _station.StationId) outputs.Add(r.Output);
            sb.Append("\nIn shared inventory: ");
            var first = true;
            foreach (var o in outputs)
            {
                sb.Append(first ? "" : ", ").Append(UnitVisuals.DisplayName(o)).Append(" ×").Append(shared.TryGetValue(o, out var q) ? q : 0);
                first = false;
            }
            return sb.ToString();
        }

        private string ProcessButtonText(ProcessRecipe r)
        {
            var p = Prod.EnsureProcessor(_station.StationId);
            var inputs = new StringBuilder();
            foreach (var i in r.Inputs) inputs.Append(inputs.Length > 0 ? " + " : "").Append(i.Value).Append(' ').Append(UnitVisuals.DisplayName(i.Key));
            var mark = p.Selected == r.Id ? "• Selected · " : p.Queued == r.Id ? "Next · " : "";
            var gate = Prod.Level(_station.StationId) < r.MinLevel ? $" · needs level {r.MinLevel}" : "";
            return $"{mark}{r.Name}: {inputs} → {r.OutputQuantity} {UnitVisuals.DisplayName(r.Output)} · {Prod.EffectiveDurationMs(r) / 1000f:0.##} s{gate}";
        }

        private static readonly int[] ReserveSteps = { 0, 2, 4, 8, 16 };

        private string ReserveText()
        {
            var p = Prod.EnsureProcessor(_station.StationId);
            var r = Prod.Process(p.Selected);
            if (r == null || r.Inputs.Count == 0) return "Reserve: n/a";
            var res = r.Inputs[0].Key;
            p.Reserves.TryGetValue(res, out var q);
            return $"Keep in reserve: {q} {UnitVisuals.DisplayName(res)} · tap to change";
        }

        private void CycleReserve()
        {
            var p = Prod.EnsureProcessor(_station.StationId);
            var r = Prod.Process(p.Selected);
            if (r == null || r.Inputs.Count == 0) return;
            var res = r.Inputs[0].Key;
            p.Reserves.TryGetValue(res, out var q);
            var i = Array.IndexOf(ReserveSteps, q);
            _root.Services.Production.SetReserve(_station.StationId, res, ReserveSteps[(i + 1) % ReserveSteps.Length]);
        }

        private string RecipeCard(CraftRecipe r)
        {
            var item = Cat.TryGet<ItemDefinition>(r.OutputItemId, out var i) ? i : null;
            var sb = new StringBuilder();
            sb.Append("<b>").Append(item != null ? item.DisplayName : r.OutputItemId).Append("</b>");
            if (item != null) sb.Append(" · ").Append(item.StatLine());
            sb.Append("\nCost: ");
            var first = true;
            foreach (var c in r.Inputs)
            {
                var have = _root.Services.Logistics.System.SharedAvailable(c.Key);
                var colour = have >= c.Value ? "#3B6E2E" : "#B44034";
                sb.Append(first ? "" : " · ").Append($"<color={colour}>{UnitVisuals.DisplayName(c.Key)} {c.Value} (have {have})</color>");
                first = false;
            }
            if (r.MinLevel > 1) sb.Append($"\nRequires station level {r.MinLevel}");
            return sb.ToString();
        }

        private string CraftButtonText(CraftRecipe r)
        {
            var reason = Prod.CraftBlockedReason(r.Id);
            return reason == null ? $"Craft {UnitVisuals.DisplayName(r.OutputItemId)}" : UnitVisuals.PrettyIds(reason);
        }
    }
}
