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
            var content = ui.ModalScroll(parent, "LoadoutLayer", out var layer, out var title);
            var panel = layer.AddComponent<LoadoutPanel>();
            panel._ui = ui;
            panel._theme = ui.Theme;
            panel._root = root;
            panel._content = content;
            panel._title = title;
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
            foreach (var t in _texts) t.text.text = t.provider();
            foreach (var b in _live)
            {
                b.label.text = b.text();
                b.button.interactable = b.enabled == null || b.enabled();
            }
        }

        private void Rebuild()
        {
            for (var i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            _live.Clear();
            _texts.Clear();
            var c = Contractor;
            _title.text = c != null ? $"Meet {c.Name}" : "Applicant";

            Text(CardText);
            Text(() => string.IsNullOrEmpty(_message) ? "" : $"<b>{_message}</b>");
            Header("Loadout (reserved while you decide)");
            Button(() => SlotText(Slot.Weapon), () => Cycle(Slot.Weapon), null);
            Button(() => SlotText(Slot.OffHand), () => Cycle(Slot.OffHand), null);
            Button(() => SlotText(Slot.Head), () => Cycle(Slot.Head), null);
            Button(() => SlotText(Slot.Armor), () => Cycle(Slot.Armor), null);
            Button(() => SlotText(Slot.Potion1), () => Cycle(Slot.Potion1), null);
            Button(() => SlotText(Slot.Potion2), () => Cycle(Slot.Potion2), null);
            Header("Review");
            Text(ForecastText);
            Button(SendText, Send, () => Board.DispatchBlockedReason(_contractorId, _loadout) == null);
            Button(() => "Back (releases the gear)", Close, null);
            Update();
        }

        private void Header(string text)
        {
            var t = _ui.Text("Header", _content, text, _theme.bodySize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
        }

        private void Text(Func<string> provider) => _texts.Add((_ui.LiveText(_content, _theme.smallSize), provider));

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
            Update();
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
                Update();
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
            Update();
        }
    }
}
