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
        private bool _upgrades;
        private Image _craftTabFace, _upgradeTabFace;
        private TextMeshProUGUI _status;

        public bool IsOpen => gameObject.activeSelf;
        public string StationId => _station != null ? _station.StationId : null;
        public string LastMessage => _lastMessage;

        public static StationPanel Create(Transform parent, UiFactory ui, GameRoot root)
        {
            var layer = UiFactory.Rect("StationLayer", parent);
            UiFactory.Stretch(layer);
            layer.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.07f, 0.05f, 0.08f);
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
            var card = WorkshopUi.Frame(transform);
            WorkshopUi.Bounds(card.rectTransform, .025f, .10f, .975f, .84f);
            _title = _ui.Text("Title", card.transform, "", _theme.titleSize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UiFactory.Place(_title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(48f, -36f), new Vector2(820f, 80f));

            var viewport = UiFactory.Rect("Viewport", card.transform);
            UiFactory.Stretch(viewport);
            // The list scrolls above a Close button pinned to the card's foot (the playtester, 2026-10-17: with the
            // Forge's many recipes the last craft button was cut off and Close scrolled out of sight).
            viewport.offsetMin = new Vector2(36f, 250f);
            viewport.offsetMax = new Vector2(-36f, -285f);
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
            _scroll = scroll;
            _viewport = viewport;

            var craftTab = WorkshopUi.Button(_ui, "CraftTab", card.transform, "Craft", () => SelectTab(false), out _);
            WorkshopUi.Bounds((RectTransform)craftTab.transform, .045f, 1, .32f, 1);
            ((RectTransform)craftTab.transform).offsetMin = new Vector2(0, -230);
            ((RectTransform)craftTab.transform).offsetMax = new Vector2(0, -130);
            _craftTabFace = craftTab.transform.Find("Face").GetComponent<Image>();
            var upgradeTab = WorkshopUi.Button(_ui, "UpgradeTab", card.transform, "Upgrade", () => SelectTab(true), out _, false);
            WorkshopUi.Bounds((RectTransform)upgradeTab.transform, .34f, 1, .64f, 1);
            ((RectTransform)upgradeTab.transform).offsetMin = new Vector2(0, -230);
            ((RectTransform)upgradeTab.transform).offsetMax = new Vector2(0, -130);
            _upgradeTabFace = upgradeTab.transform.Find("Face").GetComponent<Image>();
            _status = WorkshopUi.Label(_ui, card.transform, "", 27, .66f, 1, .96f, 1, false, TextAlignmentOptions.Center);
            _status.rectTransform.offsetMin = new Vector2(0, -230);
            _status.rectTransform.offsetMax = new Vector2(0, -130);
            var close = WorkshopUi.Button(_ui, "Close", card.transform, "Close", Close, out _, false);
            Destroy(close.GetComponent<LayoutElement>());
            UiFactory.Place(close.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0f, 42f), new Vector2(520f, 132f));
            _more = _ui.Text("MoreBelow", card.transform, "More below ▼", _theme.smallSize, _theme.ink, TextAlignmentOptions.Center, FontStyles.Bold);
            UiFactory.Place(_more.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0f, 192f), new Vector2(600f, 44f));
            _more.raycastTarget = false;
            SandboxHud.AutoFit(_more, 18f);   // the largest text setting shrinks it to fit
        }

        private ScrollRect _scroll;
        private RectTransform _viewport;
        private TextMeshProUGUI _more;

        /// <summary>Whether the list has more below what is shown (tests).</summary>
        public bool MoreBelow => _more != null && _more.gameObject.activeSelf;

        public void Show(StationInspectable station)
        {
            _station = station;
            _upgrades = false;
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
            if (_more != null && _scroll != null && _viewport != null)
            {
                var more = _content.rect.height > _viewport.rect.height + 4f && _scroll.verticalNormalizedPosition > 0.02f;
                if (_more.gameObject.activeSelf != more) _more.gameObject.SetActive(more);
            }
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
            _lastMessage = ok ? $"Started {UnitVisuals.DisplayName(Prod.Recipe(recipeId).OutputItemId)}: ready in {Duration(Prod.EffectiveCraftMs(Prod.Recipe(recipeId)))}. It goes onto the output pad when done."
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
            if (_status != null)
            {
                var def = Cat.TryGet<StationDefinition>(_station.StationId, out var sd) ? sd : null;
                _status.text = def == null ? "" : $"Output {_root.Services.Logistics.System.ContainerUnitCount(def.OutputPadContainerId)}/{def.OutputPadCapacity}\nSlots {Prod.BusySlots(_station.StationId)}/{Prod.CraftSlots(_station.StationId)}";
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
            var sb = new StringBuilder(_station.StationId).Append(_upgrades);
            sb.Append('|').Append(Prod.Level(_station.StationId));
            sb.Append('|').Append(_root.Services.Economy.Purchased.Count).Append('|').Append(Feel.DeveloperMode);
            sb.Append('|').Append(_root.Services.Sites.PlayerToolId);
            foreach (var bp in Prod.State.Blueprints) sb.Append('|').Append(bp);
            return sb.ToString();
        }

        public void SelectTab(bool upgrades)
        {
            _upgrades = upgrades; _structureKey = ""; Refresh();
            _scroll.verticalNormalizedPosition = 1;
        }

        private void Rebuild()
        {
            WorkshopUi.Clear(_content);
            _live.Clear(); _liveTexts.Clear();
            var stationId = _station.StationId;
            var stationDef = Cat.TryGet<StationDefinition>(stationId, out var sd) ? sd : null;
            _title.text = _station.Title.ToUpperInvariant();
            _craftTabFace.color = _upgrades ? WorkshopUi.Cream : WorkshopUi.Gold;
            _upgradeTabFace.color = _upgrades ? WorkshopUi.Gold : WorkshopUi.Cream;
            if (_upgrades)
            {
                if (stationDef != null) UpgradeSection(stationDef);
                if (stationDef != null && stationDef.SellsSupplierTools) ToolsSection();
            }
            else
            {
                foreach (var recipe in Prod.Recipes)
                    if (recipe.Station == stationId && Prod.State.Blueprints.Contains(recipe.BlueprintId)) AddRecipe(recipe);
                Text(() => SlotsText(stationId), _theme.smallSize);
                var processes = new List<ProcessRecipe>();
                foreach (var process in Prod.Processes) if (process.Facility == stationId) processes.Add(process);
                if (processes.Count > 0)
                {
                    Header("Processing line"); Text(ProcessText, _theme.smallSize);
                    foreach (var process in processes)
                    {
                        var captured = process;
                        Button(() => ProcessButtonText(captured), () => _root.Services.Production.SelectProcess(stationId, captured.Id),
                            () => Prod.Level(stationId) >= captured.MinLevel);
                    }
                    Button(ReserveText, CycleReserve, null);
                }
                if (_station.HasReceiver) { Header("Receiving"); Text(ReceiverText, _theme.smallSize); }
            }
            Text(() => _lastMessage, _theme.smallSize);
            if (stationDef != null && Feel.DeveloperMode)
                Button(() => $"DEBUG station level: {Prod.Level(stationId)} of {stationDef.MaxLevel} · tap to change",
                    () => _root.Services.Production.DebugCycleLevel(stationDef), null);
        }

        private void AddRecipe(CraftRecipe recipe)
        {
            var item = Cat.TryGet<ItemDefinition>(recipe.OutputItemId, out var d) ? d : null;
            var row = WorkshopUi.Surface("Recipe_" + recipe.Id, _content, WorkshopUi.Cream);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 280;
            var icon = _ui.Icon("Item", row.transform, item != null ? WorkshopUi.ItemSprite(item) : _theme.unknown, 132);
            WorkshopUi.Bounds(icon.rectTransform, .025f, .29f, .19f, .92f);
            WorkshopUi.Label(_ui, row.transform, item != null ? item.DisplayName : recipe.OutputItemId, 35, .21f, .68f, .76f, .98f, true);
            WorkshopUi.Label(_ui, row.transform, item != null ? item.StatLine() : "", 26, .21f, .45f, .76f, .70f);
            var costs = WorkshopUi.Label(_ui, row.transform, "", 25, .21f, .20f, .76f, .46f);
            var state = WorkshopUi.Label(_ui, row.transform, "", 25, .025f, .025f, .975f, .20f);
            var button = WorkshopUi.Button(_ui, "Craft", row.transform, "Craft", () => Craft(recipe.Id), out var label);
            WorkshopUi.Bounds((RectTransform)button.transform, .78f, .34f, .97f, .82f);
            _live.Add(new LiveButton { Button = button, Label = label, Text = () => Prod.Level(recipe.Station) < recipe.MinLevel ? "Locked" : "Craft",
                Enabled = () => Prod.CraftBlockedReason(recipe.Id) == null });
            _liveTexts.Add((costs, () =>
            {
                var sb = new StringBuilder();
                foreach (var cost in recipe.Inputs)
                {
                    if (sb.Length > 0) sb.Append(" · ");
                    var enough = _root.Services.Logistics.System.SharedAvailable(cost.Key) >= cost.Value;
                    sb.Append($"<color={(enough ? "#3B6E2E" : "#A64232")}>{cost.Value} {UnitVisuals.DisplayName(cost.Key)}</color>");
                }
                return sb.ToString();
            }));
            _liveTexts.Add((state, () =>
            {
                var reason = Prod.CraftBlockedReason(recipe.Id);
                return reason == null ? $"Ready · {Duration(Prod.EffectiveCraftMs(recipe))}" : UnitVisuals.PrettyIds(reason);
            }));
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

        /// <summary>
        /// The supplier's own tool (solo play: you are the only one gathering). Each site's equipment upgrade unlocks
        /// the next tool tier; only the next step of each is offered.
        /// </summary>
        private void ToolsSection()
        {
            var eco = _root.Services.Economy;
            var sites = _root.Services.Sites;
            Header("Your tools");
            Text(() => $"Your tool: {ToolName(sites.PlayerToolId)}. It sets how fast you mine and chop. " +
                       "Upgrading a site's equipment unlocks the next tool.", _theme.smallSize);

            foreach (var u in Cat.Upgrades)
            {
                if (u == null || u.Kind != UpgradeKind.SiteEquipment || u.Site == null || eco.Purchased.Contains(u.Id)) continue;
                if (u.Level != eco.SiteEquipmentLevel(u.Site.Id) + 1) continue;
                var up = u;
                Text(() => UpgradeCard(_root, up), _theme.smallSize);
                Button(() => UpgradeButtonText(_root, up), () => BuyUpgrade(up), () => eco.UpgradeBlockedReason(up) == null);
            }

            var curTier = Cat.TryGet<WorkerEquipmentDefinition>(sites.PlayerToolId, out var ce) ? ce.Tier : 1;
            foreach (var e in Cat.Equipment)
            {
                if (e == null || e.Slot != EquipmentSlot.Tool || e.Tier != curTier + 1) continue;   // only the next step
                var eq = e;
                Button(() => ToolButtonText(eq), () => BuyTool(eq), () => eco.PlayerEquipmentBlockedReason(eq) == null);
            }
        }

        private string ToolName(string id) => Cat.TryGet<WorkerEquipmentDefinition>(id, out var e) ? e.DisplayName : "bare hands";

        private string ToolButtonText(WorkerEquipmentDefinition e)
        {
            var reason = _root.Services.Economy.PlayerEquipmentBlockedReason(e);
            var cost = new StringBuilder().Append(e.GoldCost).Append(" gold");
            foreach (var c in e.MaterialCost) cost.Append(", ").Append(c.resource.DisplayName).Append(' ').Append(c.quantity);
            return reason == null ? $"Buy {e.DisplayName} ({cost})" : $"{e.DisplayName}: {reason}";
        }

        public bool BuyTool(WorkerEquipmentDefinition e)
        {
            var ok = _root.Services.Economy.BuyPlayerEquipment(e, out var failure);
            _lastMessage = ok ? $"{e.DisplayName} in hand." : failure;
            if (ok) FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
            _structureKey = "";
            Refresh();
            return ok;
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

        /// <summary>Exact effect, cost against what you have, and prerequisite.</summary>
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
            WorkshopUi.Button(_ui, "Button", _content, "", onClick, out var label);
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
            sb.Append($"\nTakes {Duration(Prod.EffectiveCraftMs(r))}");
            if (r.MinLevel > 1) sb.Append($" · requires station level {r.MinLevel}");
            return sb.ToString();
        }

        /// <summary>The station's craft slots: what is being made and how long is left (the crafting expansion).</summary>
        private string SlotsText(string stationId)
        {
            var sb = new StringBuilder($"Craft slots: {Prod.BusySlots(stationId)} of {Prod.CraftSlots(stationId)} busy");
            foreach (var j in Prod.CraftsAt(stationId))
            {
                var name = UnitVisuals.DisplayName(Prod.Recipe(j.RecipeId)?.OutputItemId ?? j.RecipeId);
                sb.Append("\n• ").Append(name).Append(j.Waiting ? ": done, waiting for room on the pad" : $": {Duration(j.EndMs - Prod.State.SimMs)} left");
            }
            return sb.ToString();
        }

        private static string Duration(long ms)
        {
            var s = Math.Max(0L, (ms + 999) / 1000);
            if (s < 60) return $"{s} s";
            if (s < 3600) return $"{s / 60} min {s % 60:00} s";
            return $"{s / 3600} h {s / 60 % 60:00} min";
        }

        private string CraftButtonText(CraftRecipe r)
        {
            var reason = Prod.CraftBlockedReason(r.Id);
            return reason == null ? $"Craft {UnitVisuals.DisplayName(r.OutputItemId)} ({Duration(Prod.EffectiveCraftMs(r))})" : UnitVisuals.PrettyIds(reason);
        }
    }
}
