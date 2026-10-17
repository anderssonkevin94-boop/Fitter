using System;
using System.Collections.Generic;
using System.Text;
using Ravenholm.Content;
using Ravenholm.Domain.Jobs;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Meet an applicant and prepare them (GDD 4/11/12): card, four slots filled from Store stock
    /// (reserved while this editor is open), the qualitative forecast with matched and unmatched threats,
    /// potion triggers, the fee, and one clear Send. Closing without sending releases every reservation.
    /// </summary>
    public sealed class LoadoutPanel : MonoBehaviour
    {
        private enum Slot { Weapon, Armor, Potion1, Potion2, OffHand, Head }

        private UiFactory _ui;
        private UiTheme _theme;
        private GameRoot _root;
        private RectTransform _content;
        private TextMeshProUGUI _title;
        private readonly List<(TextMeshProUGUI label, Button button, Func<string> text, Func<bool> enabled)> _live = new List<(TextMeshProUGUI, Button, Func<string>, Func<bool>)>();
        private readonly List<(TextMeshProUGUI text, Func<string> provider)> _texts = new List<(TextMeshProUGUI, Func<string>)>();
        private Slot _selectedSlot = Slot.Weapon;
        private RectTransform _equipment;
        private RectTransform _choices;
        private TextMeshProUGUI _summary;
        private TextMeshProUGUI _warning;
        private WorkshopHeroPreview _preview;
        private string _optionsKey = "";
        private readonly List<Action> _visualRefresh = new List<Action>();
        private string _contractorId;
        private Loadout _loadout = new Loadout();
        private string _message = "";
        private float _confirmUntil = -1f;
        private float _next;

        public bool IsOpen => gameObject.activeSelf;
        public Loadout Current => _loadout;
        public string Message => _message;

        public static LoadoutPanel Create(Transform parent, UiFactory ui, GameRoot root)
        {
            var rect = UiFactory.Rect("LoadoutLayer", parent);
            UiFactory.Stretch(rect);
            rect.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .08f);
            var layer = rect.gameObject;
            var panel = layer.AddComponent<LoadoutPanel>();
            panel._ui = ui; panel._theme = ui.Theme; panel._root = root;
            panel.Build();
            layer.SetActive(false);
            return panel;
        }

        private JobBoardSystem Board => _root.Services.Jobs.Board;
        private ContractorRecord Contractor => Board.Contractor(_contractorId);
        private QuestInfo Quest => Board.State.Posting != null ? _root.Services.Jobs.Content.Quest(Board.State.Posting.QuestId) : null;

        public void Show(string contractorId)
        {
            _contractorId = contractorId;
            _message = "";
            _confirmUntil = -1f;
            var c = Contractor;
            // Start from the strongest compatible available gear; the player can change every slot.
            _loadout = c != null ? Board.BestAvailableLoadout(c, JobBoardSystem.LoadoutOwner) : new Loadout();
            _loadout.Potion1 = FirstPotion(null);
            if (!Board.ReserveLoadout(_loadout)) { _loadout = new Loadout(); Board.ReserveLoadout(_loadout); }
            if (!IsOpen)
            {
                gameObject.SetActive(true);
                TouchInputRouter.PushModal();
            }
            _selectedSlot = Slot.Weapon;
            Rebuild();
        }

        /// <summary>Closing without dispatch releases reservations (GDD 4).</summary>
        public void Close()
        {
            if (!IsOpen) return;
            Board.ReleaseLoadout();
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
            if (Contractor == null || Board.State.Posting == null) { Close(); return; }
            RefreshEquipment();
            foreach (var t in _texts) t.text.text = t.provider();
            foreach (var b in _live)
            {
                b.label.text = b.text();
                b.button.interactable = b.enabled == null || b.enabled();
            }
        }

        private void Build()
        {
            var card = WorkshopUi.Frame(transform);
            WorkshopUi.Bounds(card.rectTransform, .025f, .055f, .975f, .91f);
            _title = WorkshopUi.Label(_ui, card.transform, "EQUIP HERO", 52, .055f, .925f, .81f, .99f, true);
            var close = WorkshopUi.Button(_ui, "Close", card.transform, "×", Close, out _, false);
            WorkshopUi.Bounds((RectTransform)close.transform, .84f, .925f, .96f, .99f);
            _summary = WorkshopUi.Label(_ui, card.transform, "", 34, .055f, .83f, .95f, .925f);
            _equipment = UiFactory.Rect("Equipment", card.transform);
            WorkshopUi.Bounds(_equipment, .05f, .48f, .95f, .83f);
            _choices = UiFactory.Rect("Choices", card.transform);
            WorkshopUi.Bounds(_choices, .05f, .27f, .95f, .48f);
            var viewport = UiFactory.Rect("ReviewViewport", card.transform);
            WorkshopUi.Bounds(viewport, .055f, .15f, .945f, .265f);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            _content = UiFactory.Rect("Review", viewport);
            _content.anchorMin = new Vector2(0, 1); _content.anchorMax = Vector2.one;
            _content.pivot = new Vector2(.5f, 1); _content.sizeDelta = Vector2.zero;
            UiFactory.Column(_content.gameObject, 12, new RectOffset(8, 8, 8, 16));
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = _content; scroll.viewport = viewport; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            _warning = WorkshopUi.Label(_ui, card.transform, "", 27, .055f, .10f, .945f, .145f, true, TextAlignmentOptions.Center);
            var back = WorkshopUi.Button(_ui, "Back", card.transform, "Back", Close, out _, false);
            WorkshopUi.Bounds((RectTransform)back.transform, .055f, .025f, .33f, .095f);
            var send = WorkshopUi.Button(_ui, "Send", card.transform, "", Send, out var sendLabel);
            WorkshopUi.Bounds((RectTransform)send.transform, .35f, .025f, .945f, .095f);
            _live.Add((sendLabel, send, SendText, () => Board.DispatchBlockedReason(_contractorId, _loadout) == null));
        }

        private void Rebuild()
        {
            WorkshopUi.Clear(_content);
            WorkshopUi.Clear(_equipment);
            _texts.Clear(); _visualRefresh.Clear(); _optionsKey = "";
            var c = Contractor;
            var cls = c != null && _root.Services.Catalogue.TryGet<AdventurerClassDefinition>(c.ClassId, out var cd) ? cd : null;
            var stats = c != null ? Board.BaseStats(c) : default;
            _summary.text = c == null ? "Applicant" : $"<b>{c.Name}</b> · {cls?.DisplayName} · Level {c.Level}\nMight {stats.Might}    Survival {stats.Survival}    Insight {stats.Insight}";
            var portrait = UiFactory.Rect("HeroPreview", _equipment);
            WorkshopUi.Bounds(portrait, .30f, .06f, .70f, .99f);
            _preview = portrait.gameObject.AddComponent<WorkshopHeroPreview>();
            WorkshopUi.Label(_ui, _equipment, "Hero", 26, .34f, 0, .66f, .08f, false, TextAlignmentOptions.Center);
            AddSlot(Slot.Head, 0, .68f); AddSlot(Slot.Weapon, 0, .35f); AddSlot(Slot.Armor, 0, .02f);
            AddSlot(Slot.OffHand, .71f, .68f); AddSlot(Slot.Potion1, .71f, .35f); AddSlot(Slot.Potion2, .71f, .02f);
            Text(() => string.IsNullOrEmpty(_message) ? "" : $"<b>{_message}</b>");
            Text(() => $"<b>{Quest?.Name}</b> · Review ↓");
            Text(ForecastText);
            Text(CardText); // Keep traits, history and the exact fee available in the scrolling review.
            RefreshEquipment();
            _next = 0; Update();
        }

        private static string SlotName(Slot slot) => slot switch
        {
            Slot.Weapon => "Weapon", Slot.Armor => "Body", Slot.Head => "Head", Slot.OffHand => "Off-hand",
            Slot.Potion1 => "Potion 1", _ => "Potion 2"
        };

        private ItemDefinition Definition(string instance)
        {
            var id = instance == null ? null : _root.Services.Logistics.System.DefinitionOfItem(instance);
            return id != null && _root.Services.Catalogue.TryGet<ItemDefinition>(id, out var item) ? item : null;
        }

        private void AddSlot(Slot slot, float x, float y)
        {
            var button = WorkshopUi.Button(_ui, "Slot" + slot, _equipment, "", () => SelectSlot((int)slot), out var label, false);
            WorkshopUi.Bounds((RectTransform)button.transform, x, y, x + .29f, y + .30f, 3);
            WorkshopUi.Label(_ui, button.transform, SlotName(slot), 29, .02f, .74f, .98f, 1, true, TextAlignmentOptions.Center);
            WorkshopUi.Bounds(label.rectTransform, .015f, .01f, .985f, .3f);
            label.fontSizeMax = 27; label.fontSizeMin = 22;
            var icon = _ui.Icon("Item", button.transform, null, 100);
            WorkshopUi.Bounds(icon.rectTransform, .18f, .30f, .82f, .76f);
            var face = button.transform.Find("Face").GetComponent<Image>();
            _visualRefresh.Add(() =>
            {
                var item = Definition(Get(slot));
                icon.sprite = item != null ? WorkshopUi.ItemSprite(item) : EmptySprite(slot); icon.enabled = icon.sprite != null; icon.color = item == null ? new Color(.5f, .45f, .38f, .24f) : Color.white;
                label.text = item != null ? item.DisplayName : "Empty";
                if (slot == Slot.OffHand && Info(_loadout.Weapon) is ItemInfo weapon && weapon.TwoHanded) label.text = "Two-handed";
                face.color = _selectedSlot == slot ? GoldTint : WorkshopUi.Cream;
            });
        }

        private Sprite EmptySprite(Slot slot)
        {
            var category = slot == Slot.Weapon ? ItemCategory.Weapon : slot == Slot.Armor ? ItemCategory.Armor
                : slot == Slot.Head ? ItemCategory.Helmet : slot == Slot.OffHand ? ItemCategory.Shield : ItemCategory.Potion;
            foreach (var item in _root.Services.Catalogue.Items)
                if (item != null && item.Category == category) return WorkshopUi.ItemSprite(item);
            return null;
        }

        private static Color GoldTint => new Color32(244, 211, 143, 255);

        public void SelectSlot(int slotIndex)
        {
            if (!IsOpen || !Enum.IsDefined(typeof(Slot), slotIndex)) return;
            _selectedSlot = (Slot)slotIndex; _optionsKey = ""; RefreshEquipment();
        }

        private void RefreshEquipment()
        {
            if (_warning != null) _warning.text = Quest != null && Quest.Recovery ? "Safe survey · No death outcome" : "Gear is lost if the hero dies.";
            foreach (var refresh in _visualRefresh) refresh();
            if (_preview != null) _preview.SetEquipment(Definition(_loadout.Head), Definition(_loadout.Armor),
                Definition(_loadout.Weapon), Definition(_loadout.OffHand));
            var options = Options(_selectedSlot);
            var key = _selectedSlot + "|" + Get(_selectedSlot) + "|" + string.Join(",", options);
            if (key == _optionsKey) return;
            _optionsKey = key;
            WorkshopUi.Clear(_choices);
            WorkshopUi.Label(_ui, _choices, "CHOOSE " + SlotName(_selectedSlot).ToUpperInvariant(), 33, 0, .79f, .65f, 1, true);
            WorkshopUi.Label(_ui, _choices, "Store stock · swipe →", 25, .65f, .79f, 1, 1);
            var view = UiFactory.Rect("ItemViewport", _choices);
            WorkshopUi.Bounds(view, 0, .22f, 1, .79f);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = Color.clear;
            var content = UiFactory.Rect("Items", view);
            content.anchorMin = Vector2.zero; content.anchorMax = Vector2.up; content.pivot = new Vector2(0, .5f);
            content.sizeDelta = new Vector2(0, 0);
            var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 14; layout.childControlHeight = true; layout.childForceExpandHeight = true;
            layout.childControlWidth = true; layout.childForceExpandWidth = false;
            content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content; scroll.viewport = view; scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            options.Add(null);
            foreach (var instance in options)
            {
                var captured = instance;
                var item = Definition(instance);
                var chosen = Get(_selectedSlot) == instance;
                var button = WorkshopUi.Button(_ui, "ChooseItem", content, "", () => EquipSelected(captured), out var label, chosen);
                var le = button.GetComponent<LayoutElement>(); le.minWidth = le.preferredWidth = 260; le.minHeight = 0;
                WorkshopUi.Bounds(label.rectTransform, 0, 0, 1, .34f);
                label.text = (chosen ? "• " : "") + (item != null ? item.DisplayName : "Unequip");
                label.fontSizeMax = 28; label.fontSizeMin = 22;
                if (item != null)
                {
                    var icon = _ui.Icon("Icon", button.transform, WorkshopUi.ItemSprite(item), 100);
                    WorkshopUi.Bounds(icon.rectTransform, .2f, .35f, .8f, .98f);
                }
            }
            var selected = Definition(Get(_selectedSlot));
            WorkshopUi.Label(_ui, _choices, selected != null ? selected.StatLine() : "Choose an item from your available stock.", 27, 0, 0, 1, .22f);
        }

        /// <summary>Choose a specific compatible instance, preserving reservations and the two-handed rule.</summary>
        public bool EquipSelected(string instance)
        {
            if (!IsOpen || Contractor == null || Quest == null) return false;
            if (instance != null && !Options(_selectedSlot).Contains(instance))
            {
                _message = "That item is no longer available for this slot.";
                _next = 0; Update(); return false;
            }
            var candidate = Copy(_loadout); Set(candidate, _selectedSlot, instance);
            if (_selectedSlot == Slot.Weapon && Info(candidate.Weapon) is ItemInfo weapon && weapon.TwoHanded) candidate.OffHand = null;
            var ok = Board.ReserveLoadout(candidate);
            if (ok) { _loadout = candidate; _message = ""; }
            else _message = "That item was just reserved elsewhere.";
            _confirmUntil = -1; _optionsKey = ""; _next = 0; Update();
            return ok;
        }

        private void Header(string text)
        {
            var t = _ui.Text("Header", _content, text, _theme.bodySize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
        }

        private void Text(Func<string> provider) => _texts.Add((_ui.Text("Text", _content, "", _theme.smallSize,
            _theme.ink, TextAlignmentOptions.TopLeft), provider));

        private void Button(Func<string> text, Action onClick, Func<bool> enabled)
        {
            var b = _ui.RowButton(_content, onClick, out var label);
            _live.Add((label, b, text, enabled));
        }

        // ------------------------------------------------------------------ slots

        /// <summary>Cycles a slot through distinct, compatible, available item types, then empty.</summary>
        public void Cycle(int slotIndex) => Cycle((Slot)slotIndex);

        private void Cycle(Slot slot)
        {
            var options = Options(slot);
            var current = Get(slot);
            var currentDef = current != null ? _root.Services.Logistics.System.DefinitionOfItem(current) : null;
            var i = options.FindIndex(o => _root.Services.Logistics.System.DefinitionOfItem(o) == currentDef);
            var next = options.Count == 0 ? null : i + 1 < options.Count ? options[i + 1] : null;
            var candidate = Copy(_loadout);
            Set(candidate, slot, next);
            // A two-handed weapon leaves no hand for a shield.
            if (slot == Slot.Weapon && candidate.OffHand != null && Info(candidate.Weapon) is ItemInfo w && w.TwoHanded) candidate.OffHand = null;
            if (Board.ReserveLoadout(candidate)) _loadout = candidate;
            else _message = "That item was just reserved elsewhere.";
            _confirmUntil = -1f;
            _next = 0; Update();
        }

        /// <summary>One instance per distinct definition that fits the slot and is not in another slot or reserved elsewhere.</summary>
        private List<string> Options(Slot slot)
        {
            var log = _root.Services.Logistics.System;
            var content = _root.Services.Jobs.Content;
            var c = Contractor;
            var used = new HashSet<string>(_loadout.All());
            var mine = Get(slot);
            if (mine != null) used.Remove(mine);
            var seen = new HashSet<string>();
            var list = new List<string>();
            foreach (var inst in log.StoreItems)
            {
                if (used.Contains(inst) || log.IsReservedByOther(inst, JobBoardSystem.LoadoutOwner)) continue;
                var def = log.DefinitionOfItem(inst);
                var info = content.Item(def);
                if (info == null || seen.Contains(def)) continue;
                var fits = slot == Slot.Weapon ? info.Kind == ItemKind.Weapon && Board.CanWield(c, info)
                         : slot == Slot.Armor ? info.Kind == ItemKind.Armor
                         : slot == Slot.Head ? info.Kind == ItemKind.Helmet
                         : slot == Slot.OffHand ? info.Kind == ItemKind.Shield && !(Info(_loadout.Weapon) is ItemInfo w && w.TwoHanded)
                         : info.Kind == ItemKind.Potion || info.Kind == ItemKind.Food;   // consumables: potions and food
                if (!fits) continue;
                seen.Add(def);
                list.Add(inst);
            }
            return list;
        }

        private string FirstPotion(string except)
        {
            var log = _root.Services.Logistics.System;
            foreach (var inst in log.StoreItems)
            {
                if (inst == except || log.IsReservedByOther(inst, JobBoardSystem.LoadoutOwner)) continue;
                var info = _root.Services.Jobs.Content.Item(log.DefinitionOfItem(inst));
                if (info != null && info.Kind == ItemKind.Potion) return inst;
            }
            return null;
        }

        private ItemInfo Info(string inst) => inst == null ? null : _root.Services.Jobs.Content.Item(_root.Services.Logistics.System.DefinitionOfItem(inst));

        private string Get(Slot s) => s switch
        {
            Slot.Weapon => _loadout.Weapon,
            Slot.OffHand => _loadout.OffHand,
            Slot.Head => _loadout.Head,
            Slot.Armor => _loadout.Armor,
            Slot.Potion1 => _loadout.Potion1,
            _ => _loadout.Potion2,
        };

        private static void Set(Loadout l, Slot s, string v)
        {
            switch (s)
            {
                case Slot.Weapon: l.Weapon = v; break;
                case Slot.Armor: l.Armor = v; break;
                case Slot.OffHand: l.OffHand = v; break;
                case Slot.Head: l.Head = v; break;
                case Slot.Potion1: l.Potion1 = v; break;
                default: l.Potion2 = v; break;
            }
        }

        private static Loadout Copy(Loadout l) =>
            new Loadout { Weapon = l.Weapon, OffHand = l.OffHand, Head = l.Head, Armor = l.Armor, Potion1 = l.Potion1, Potion2 = l.Potion2 };

        private string SlotText(Slot slot)
        {
            var inst = Get(slot);
            var label = slot switch
            {
                Slot.Weapon => "Weapon", Slot.OffHand => "Off-hand", Slot.Head => "Head", Slot.Armor => "Body",
                Slot.Potion1 => "Potion 1", _ => "Potion 2",
            };
            if (slot == Slot.OffHand && Info(_loadout.Weapon) is ItemInfo two && two.TwoHanded) return "Off-hand: none (two-handed weapon)";
            if (inst == null) return $"{label}: none · tap to choose";
            var def = _root.Services.Logistics.System.DefinitionOfItem(inst);
            var item = _root.Services.Catalogue.TryGet<ItemDefinition>(def, out var d) ? d : null;
            return $"{label}: {item?.DisplayName} ({item?.StatLine()}) · tap to change";
        }

        // ------------------------------------------------------------------ text

        private string CardText()
        {
            var c = Contractor;
            var p = Board.State.Posting;
            if (c == null || p == null) return "";
            var a = p.Applicants.Find(x => x.ContractorId == c.Id);
            var cls = _root.Services.Catalogue.TryGet<AdventurerClassDefinition>(c.ClassId, out var cd) ? cd : null;
            var s = Board.BaseStats(c);
            var sb = new StringBuilder();
            sb.Append($"<b>{c.Name}</b> · {cls?.DisplayName} · level {c.Level}\n");
            sb.Append($"Might {s.Might} · Survival {s.Survival} · Insight {s.Insight}");
            foreach (var t in c.Traits)
                if (_root.Services.Catalogue.TryGet<TraitDefinition>(t, out var td)) sb.Append($"\n<b>{td.DisplayName}</b>: {td.Description}");
            if (cls != null && !string.IsNullOrEmpty(cls.Role)) sb.Append($"\n<size=85%>{cls.Role}</size>");
            sb.Append($"\nHistory: {JobBoardPanel.FullHistory(c)}");
            var q = Quest;
            var byCastle = Board.State.Posting != null && Board.State.Posting.ByCastle;
            if (q != null && a != null && byCastle && !q.Recovery)
                sb.Append($"\nFor <b>{q.Name}</b> (a castle job): the castle pays them. You lend the gear for <b>{Board.Config.ShopSharePercent}%</b> " +
                          $"of the reward: about {q.BaseGold * Board.Config.ShopSharePercent / 100} g if it goes well. The gear comes back unless they die.");
            else if (q != null && a != null)
                sb.Append(q.Recovery
                    ? $"\nFor <b>{q.Name}</b>: quotes {a.Fee} g, <b>paid by the patron</b> (you pay nothing)."
                    : $"\nFor <b>{q.Name}</b>: quotes <b>{a.Fee} g</b> (you have {Board.State.Gold} g). Quotes do not change while you decide.");
            return sb.ToString();
        }

        /// <summary>A shape for each risk word, so the forecast reads without colour (GDD 19).</summary>
        public static string BandMarker(RiskBand b) =>
            b == RiskBand.Excellent ? "[++]" : b == RiskBand.Good ? "[+]" : b == RiskBand.Risky ? "[~]" : b == RiskBand.Dangerous ? "[!]" : "[!!]";

        private string ForecastText()
        {
            var c = Contractor;
            var q = Quest;
            if (c == null || q == null) return "";
            var sb = new StringBuilder();
            if (q.Recovery)
            {
                sb.Append("<b>Safe survey</b>: no danger, no death outcome. Pays a little and rebuilds reputation slowly.");
                return sb.ToString();
            }
            var f = Board.Forecast(c, _loadout, q);
            sb.Append($"Forecast: <b>{BandMarker(f.Band)} {f.Band}</b>{(f.LimitedKnowledge ? " · Limited knowledge (provisional)" : "")}\n");
            foreach (var t in f.Threats)
            {
                var name = UnitVisuals.DisplayName(t.Threat.Id);
                switch (t.Status)
                {
                    // GDD 19: never colour alone. Each state also has its own marker and word.
                    case ThreatStatus.Countered: sb.Append($"[+] {name}: <color=#3B6E2E>countered</color>\n"); break;
                    case ThreatStatus.Helps: sb.Append($"[~] {name}: helps (+{t.Supplied} of {t.Threat.Required} needed), still a risk\n"); break;
                    default: sb.Append($"[!] {name}: <color=#B44034>not countered</color>\n"); break;
                }
            }
            foreach (var t in q.Threats) if (!t.Known) sb.Append("[?] Threat not yet identified\n");
            if (f.LimitedKnowledge) sb.Append("Unknown threats may make this estimate optimistic.\n");
            foreach (var p in new[] { _loadout.Potion1, _loadout.Potion2 })
            {
                if (p == null) continue;
                var info = _root.Services.Jobs.Content.Item(_root.Services.Logistics.System.DefinitionOfItem(p));
                if (info == null) continue;
                sb.Append(info.Kind == ItemKind.Food
                    ? $"• {UnitVisuals.DisplayName(info.Id)}: eaten on the road (well fed: +{info.FoodSurvival} survival, +{info.FoodReadiness} readiness)\n"
                    : info.SurvivalBonus > 0
                        ? $"• {UnitVisuals.DisplayName(info.Id)}: used only if things go badly (+{info.SurvivalBonus} survival)\n"
                        : $"• {UnitVisuals.DisplayName(info.Id)}: used if {info.CounterTag.ToLowerInvariant()} is present\n");
            }
            if (Info(_loadout.OffHand) is ItemInfo sh && sh.Kind == ItemKind.Shield)
                sb.Append($"• {UnitVisuals.DisplayName(sh.Id)}: may block a killing blow ({sh.Block}%)\n");
            sb.Append("<b>Gear is permanently lost if the contractor dies.</b> Unused potions come back on any other return.");
            // Designer readout only (GDD 12: players never see exact odds, and this includes hidden threats).
            if (Feel.DeveloperMode)
            {
                var terms = Board.Forecast(c, _loadout, q);
                sb.Append($"\n<size=75%><color=#7A6A5A>DEBUG terms: A {terms.Readiness:0.0} (C {terms.Context:+0.0;-0.0}, route {q.RouteBonus}) · D {q.Difficulty} · margin {terms.MeanMargin:+0.0;-0.0} · V before K {terms.SurvivalWithoutRoll:+0.0;-0.0} (survival ctx {terms.SurvivalContext:+0;-0})</color></size>");
            }
            if (Feel.DeveloperMode)
                sb.Append($"\n<size=75%><color=#7A6A5A>DEBUG exact odds (2000 seeds, incl. hidden threats): {Distribution(c, q)}</color></size>");
            return sb.ToString();
        }

        private string _distKey = "";
        private string _distText = "";

        /// <summary>Designer-only exact distribution (GDD 12: never shown as player-facing percentages).</summary>
        private string Distribution(ContractorRecord c, QuestInfo q)
        {
            var key = $"{c.Id}|{q.Id}|{_loadout.Weapon}|{_loadout.OffHand}|{_loadout.Head}|{_loadout.Armor}|{_loadout.Potion1}|{_loadout.Potion2}";
            if (key == _distKey) return _distText;
            _distKey = key;
            var stats = Board.StatsWith(c, _loadout);
            var all = _root.Services.Jobs.Content.Quest(q.Id);
            var potions = new List<CarriedPotion>();
            foreach (var p in new[] { _loadout.Potion1, _loadout.Potion2 })
            {
                var info = p != null ? _root.Services.Jobs.Content.Item(_root.Services.Logistics.System.DefinitionOfItem(p)) : null;
                if (info != null) potions.Add(new CarriedPotion { InstanceId = p, DefinitionId = info.Id, SurvivalBonus = info.SurvivalBonus, CounterTag = info.CounterTag, Food = info.Kind == ItemKind.Food });
            }
            var counts = new int[5];
            for (var i = 0; i < 2000; i++)
                counts[(int)Resolver.Resolve(Board.Config.Resolver, stats, q.Difficulty, all.Threats.FindAll(t => !q.PreparedThreats.Contains(t.Id)), potions, (ulong)(i * 2654435761L + 17), null, q.RouteBonus).Outcome]++;
            _distText = $"Great {counts[4] / 20f:0}% · Success {counts[3] / 20f:0}% · Partial {counts[2] / 20f:0}% · Fail {counts[1] / 20f:0}% · Death {counts[0] / 20f:0}%";
            return _distText;
        }

        private string SendText()
        {
            var reason = Board.DispatchBlockedReason(_contractorId, _loadout);
            if (reason != null) return UnitVisuals.PrettyIds(reason);
            var c = Contractor;
            var a = Board.State.Posting.Applicants.Find(x => x.ContractorId == _contractorId);
            var fee = Quest.Recovery ? "patron pays" : Board.State.Posting.ByCastle ? "lend the gear" : $"{a.Fee} g";
            if (!Board.State.FirstDispatchDone && Time.unscaledTime < _confirmUntil)
                return "Tap again to send: gear is lost if they die";
            return $"Send {c.Name} ({fee})";
        }

        /// <summary>One clear confirmation; the very first dispatch asks twice because of permanent loss (GDD 11).</summary>
        public void Send()
        {
            if (!Board.State.FirstDispatchDone && Time.unscaledTime > _confirmUntil)
            {
                _confirmUntil = Time.unscaledTime + 3f;
                _next = 0; Update();
                return;
            }
            var name = Contractor?.Name;
            if (_root.Services.Jobs.Dispatch(_contractorId, _loadout, out var failure, out var e))
            {
                FeedbackAudio.Play(FeedbackAudio.Cue.Dispatch);
                Board.ReleaseLoadout();
                gameObject.SetActive(false);
                TouchInputRouter.PopModal();
                _root.Hud.ShowToast($"{name} departs");
                return;
            }
            _message = UnitVisuals.PrettyIds(failure);
            _next = 0; Update();
        }
    }
}
