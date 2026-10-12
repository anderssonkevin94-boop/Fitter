using System;
using System.Collections.Generic;
using System.Text;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Mine / Forest site panel (GDD 6/7/19). Mine draws a strata column (past layers compressed,
    /// current face, unknown depth below); Forest draws a trail of areas. Both share the decision
    /// block: current composition, effort increase, and the uncertainty of going deeper.
    /// Decisions are commands to the sites service; the panel never edits state.
    /// </summary>
    public sealed class SitePanel : MonoBehaviour
    {
        private UiFactory _ui;
        private UiTheme _theme;
        private GameRoot _root;
        private string _siteId;
        private TextMeshProUGUI _title;
        private RectTransform _diagram;
        private TextMeshProUGUI _status;
        private TextMeshProUGUI _decisionText;
        private Button _continue;
        private TextMeshProUGUI _continueLabel;
        private Button _deeper;
        private TextMeshProUGUI _deeperLabel;
        private GameObject _decisionBlock;
        private TextMeshProUGUI _toolLabel;
        private TextMeshProUGUI _packLabel;
        private float _deeperArmedUntil = -1f;
        private float _nextRefresh;
        private string _diagramKey = "";

        public bool IsOpen => gameObject.activeSelf;
        public string SiteId => _siteId;
        public string StatusText => _status != null ? _status.text : "";

        public static SitePanel Create(Transform parent, UiFactory ui, GameRoot root)
        {
            var layer = UiFactory.Rect("SiteLayer", parent);
            UiFactory.Stretch(layer);
            layer.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.07f, 0.05f, 0.4f);
            var panel = layer.gameObject.AddComponent<SitePanel>();
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

            var viewport = UiFactory.Rect("Viewport", card.transform);
            UiFactory.Stretch(viewport);
            viewport.offsetMin = new Vector2(36f, 36f);
            viewport.offsetMax = new Vector2(-36f, -36f);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            var content = UiFactory.Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            UiFactory.Column(content.gameObject, 18f, new RectOffset(12, 12, 8, 24));
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            _title = _ui.Text("Title", content, "", _theme.titleSize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _title.gameObject.AddComponent<LayoutElement>().preferredHeight = 76f;

            _diagram = UiFactory.Rect("Diagram", content);
            var dle = _diagram.gameObject.AddComponent<LayoutElement>();
            dle.preferredHeight = 560f;

            _status = _ui.Text("Status", content, "", _theme.smallSize, _theme.ink, TextAlignmentOptions.TopLeft);

            _decisionBlock = UiFactory.Rect("Decision", content).gameObject;
            UiFactory.Column(_decisionBlock, 14f, new RectOffset(0, 0, 8, 8));
            _decisionText = _ui.Text("Text", _decisionBlock.transform, "", _theme.bodySize - 4f, _theme.ink, TextAlignmentOptions.TopLeft);
            _continue = _ui.Button("Continue", _decisionBlock.transform, "", OnContinue, out _continueLabel, 170f);
            _deeper = _ui.Button("Deeper", _decisionBlock.transform, "", OnDeeper, out _deeperLabel, 170f);
            _continueLabel.fontSize = _deeperLabel.fontSize = _theme.smallSize;
            _continueLabel.enableAutoSizing = _deeperLabel.enableAutoSizing = true;
            _continueLabel.fontSizeMin = _deeperLabel.fontSizeMin = 20f;
            _continueLabel.fontSizeMax = _deeperLabel.fontSizeMax = _theme.smallSize;

            _tunnels = UiFactory.Rect("Tunnels", content);
            UiFactory.Column(_tunnels.gameObject, 14f, new RectOffset(0, 0, 8, 8));
            _facilities = UiFactory.Rect("Facilities", content);
            UiFactory.Column(_facilities.gameObject, 14f, new RectOffset(0, 0, 8, 8));

            _ui.Button("Tool", content, "", () => Cycle(EquipmentSlot.Tool), out _toolLabel);
            _ui.Button("Pack", content, "", () => Cycle(EquipmentSlot.Pack), out _packLabel);
            _toolLabel.fontSize = _packLabel.fontSize = _theme.smallSize;
            _ui.Button("Close", content, "Close", Close, out _);
        }

        // ------------------------------------------------------------------ site facilities (GDD 16)

        private RectTransform _facilities;
        private string _facilitiesKey = "";
        private readonly List<(Button button, TextMeshProUGUI label, Func<string> text, Func<bool> enabled)> _facilityButtons =
            new List<(Button, TextMeshProUGUI, Func<string>, Func<bool>)>();
        private readonly List<(TextMeshProUGUI text, Func<string> provider)> _facilityTexts = new List<(TextMeshProUGUI, Func<string>)>();
        public string LastMessage { get; private set; } = "";

        private void RefreshFacilities(SiteSimulator sim)
        {
            var eco = _root.Services.Economy;
            var sites = _root.Services.Sites;
            var key = new StringBuilder(_siteId).Append('|').Append(eco.Purchased.Count).Append('|').Append(sites.PlayerToolId).Append(sites.PlayerPackId)
                .Append('|').Append(sites.RoutineSiteId).Append('|').Append(sim.Record.Tunnels.Count).Append('|').Append(sim.ShaftDone).Append('|').Append(sim.Record.Status);
            foreach (var t in sim.Record.Tunnels) key.Append(t.Status);
            if (key.ToString() != _facilitiesKey)
            {
                _facilitiesKey = key.ToString();
                RebuildFacilities(sim);
            }
            foreach (var t in _facilityTexts) t.text.text = t.provider();
            foreach (var b in _facilityButtons)
            {
                b.label.text = b.text();
                b.button.interactable = b.enabled == null || b.enabled();
            }
        }

        private void RebuildFacilities(SiteSimulator sim)
        {
            for (var i = _facilities.childCount - 1; i >= 0; i--) Destroy(_facilities.GetChild(i).gameObject);
            _facilityButtons.Clear();
            _facilityTexts.Clear();
            var eco = _root.Services.Economy;
            var cat = _root.Services.Catalogue;
            var siteId = _siteId;

            // While you're away (solo play): put yourself on one job; the simulation keeps doing it, online and offline.
            var rh = _ui.Text("Header", _facilities, "While you're away", _theme.bodySize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            rh.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
            var sites = _root.Services.Sites;
            FacilityText(() => sites.RoutineSiteId == null
                ? "Leave yourself on one job and it carries on while you're away (up to 8 hours), at the ordinary rate. Move to take over again."
                : sites.RoutineSiteId == siteId ? "You're on a routine here. Move (or stop it below) to take over." : "You're on a routine at the other site.");
            if (sites.RoutineSiteId == siteId) FacilityButton(() => "Stop the routine", () => { sites.EndRoutine(); _facilitiesKey = ""; Refresh(); }, null);
            else if (sim.GroveMode)
            {
                if (sim.Record.LastChop.HasValue) RoutineButton("Keep cutting near your last tree", 0);
                else FacilityText(() => "Cut a tree first: a routine carries on with the trees nearest the one you last cut.");
            }
            else if (sim.DigMode)
            {
                if (sim.Record.LastDig != null) RoutineButton("Keep digging your last tunnel", 0);
                else FacilityText(() => "Dig somewhere first: a routine carries on the tunnel you last dug.");
            }
            else if (sim.Config.Kind == SiteKind.Mine)
            {
                if (!sim.ShaftDone) RoutineButton("Keep mining the shaft", 0);
                for (var i = 0; i < sim.Record.Tunnels.Count; i++)
                    if (sim.Record.Tunnels[i].Status == SiteStatus.Working) RoutineButton($"Keep mining the level {sim.Record.Tunnels[i].Level} tunnel", i + 1);
            }
            else if (sim.Record.Status == SiteStatus.Working) RoutineButton("Keep cutting this clearing", 0);

            var h = _ui.Text("Header", _facilities, "Site equipment and your gear", _theme.bodySize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            h.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
            FacilityText(() => $"Site equipment level {eco.SiteEquipmentLevel(siteId)}. Upgrading unlocks better tools and packs; buy them for yourself below. " +
                               "Your tool sets how fast you mine and chop." +
                               (string.IsNullOrEmpty(LastMessage) ? "" : $"\n<b>{UnitVisuals.PrettyIds(LastMessage)}</b>"));

            foreach (var u in cat.Upgrades)
            {
                if (u == null || u.Site == null || u.Site.Id != siteId || eco.Purchased.Contains(u.Id)) continue;
                if (u.Kind == UpgradeKind.SiteEquipment && u.Level != eco.SiteEquipmentLevel(siteId) + 1) continue;
                var up = u;
                FacilityText(() => StationPanel.UpgradeCard(_root, up));
                FacilityButton(() => StationPanel.UpgradeButtonText(_root, up), () => BuyUpgrade(up), () => eco.UpgradeBlockedReason(up) == null);
            }

            foreach (var e in cat.Equipment)
            {
                if (e == null || e.Tier <= 1) continue;
                var cur = e.Slot == EquipmentSlot.Tool ? sites.PlayerToolId : sites.PlayerPackId;
                var curTier = cat.TryGet<WorkerEquipmentDefinition>(cur, out var ce) ? ce.Tier : 1;
                if (e.Tier != curTier + 1) continue;   // only the next step is offered
                var eq = e;
                FacilityButton(() => EquipmentButtonText(eq), () => BuyEquipment(eq), () => eco.PlayerEquipmentBlockedReason(eq) == null);
            }
        }

        private void RoutineButton(string text, int face)
        {
            var site = _siteId;
            FacilityButton(() => text, () =>
            {
                _root.Services.Sites.StartRoutine(site, face);
                LastMessage = "Routine set. Move to take over again.";
                _facilitiesKey = "";
                Close();
            }, null);
        }

        private string EquipmentButtonText(WorkerEquipmentDefinition e)
        {
            var unit = e.Slot == EquipmentSlot.Tool ? " damage/s" : " units (routine pack)";
            var cost = new StringBuilder($"{e.GoldCost} gold");
            foreach (var c in e.MaterialCost) cost.Append($" + {c.quantity} {c.resource.DisplayName}");
            var reason = _root.Services.Economy.PlayerEquipmentBlockedReason(e);
            return reason == null ? $"Buy {e.DisplayName} ({e.Value}{unit}) · {cost}" : $"{e.DisplayName}: {reason}";
        }

        public bool BuyUpgrade(UpgradeDefinition u)
        {
            var ok = _root.Services.Economy.BuyUpgrade(u, out var failure);
            LastMessage = ok ? $"{u.DisplayName} done. {u.Effect}." : failure;
            if (ok) FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
            _facilitiesKey = "";
            Refresh();
            return ok;
        }

        public bool BuyEquipment(WorkerEquipmentDefinition e)
        {
            var ok = _root.Services.Economy.BuyPlayerEquipment(e, out var failure);
            LastMessage = ok ? $"{e.DisplayName} bought and equipped." : failure;
            if (ok) FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
            _facilitiesKey = "";
            Refresh();
            return ok;
        }

        private void FacilityText(Func<string> provider)
        {
            var t = _ui.LiveText(_facilities, _theme.smallSize);
            _facilityTexts.Add((t, provider));
        }

        private void FacilityButton(Func<string> text, Action onClick, Func<bool> enabled)
        {
            var b = _ui.RowButton(_facilities, onClick, out var label);
            _facilityButtons.Add((b, label, text, enabled));
        }


        // ------------------------------------------------------------------ Mine tunnels and who digs where (GDD 6)

        private RectTransform _tunnels;
        private string _tunnelsKey = "";
        private readonly List<(Button button, TextMeshProUGUI label, Func<string> text, Func<bool> enabled)> _tunnelButtons =
            new List<(Button, TextMeshProUGUI, Func<string>, Func<bool>)>();
        private readonly List<(TextMeshProUGUI text, Func<string> provider)> _tunnelTexts = new List<(TextMeshProUGUI, Func<string>)>();

        private void RefreshTunnels(SiteSimulator sim)
        {
            var mine = sim.Config.Kind == SiteKind.Mine && !sim.DigMode;   // the dig slab has no tunnels to manage
            _tunnels.gameObject.SetActive(mine);
            if (!mine) return;
            var key = $"{_siteId}|{sim.Record.Workers.Count}|{sim.Record.Tunnels.Count}";
            if (key != _tunnelsKey)
            {
                _tunnelsKey = key;
                RebuildTunnels(sim);
            }
            foreach (var t in _tunnelTexts) t.text.text = t.provider();
            foreach (var b in _tunnelButtons)
            {
                b.label.text = b.text();
                b.button.interactable = b.enabled == null || b.enabled();
            }
        }

        private void RebuildTunnels(SiteSimulator sim)
        {
            for (var i = _tunnels.childCount - 1; i >= 0; i--) Destroy(_tunnels.GetChild(i).gameObject);
            _tunnelButtons.Clear();
            _tunnelTexts.Clear();
            var h = _ui.Text("Header", _tunnels, "Tunnels", _theme.bodySize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            h.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
            TunnelText(() => Sim == null || Sim.Record.Tunnels.Count == 0
                ? "Each layer is three levels deep and every level has its own tunnel, dug left under the Forest in that layer, " +
                  "so several miners can work one layer side by side. Continue Here starts one, or climb down the ladder and walk to the left end of a level's ledge."
                : "Each tunnel advances one stretch per Continue; when a stretch is cleared its miner waits for you. Send miners between the shaft and the tunnels here.");

            foreach (var w in sim.Record.Workers)
            {
                var id = w.Id;
                TunnelButton(() =>
                {
                    var cur = Sim?.Record.Workers.Find(x => x.Id == id);
                    if (cur == null) return "";
                    var where = WorkerView.FaceName(Sim, cur.Face);
                    return Sim.Record.Tunnels.Count == 0 ? $"{cur.Name} digs {where}" : $"{cur.Name} digs {where} · send to {WorkerView.FaceName(Sim, NextFace(cur))}";
                }, () => SendMiner(id), () => Sim != null && Sim.Record.Tunnels.Count > 0);
            }
            for (var i = 0; i < sim.Record.Tunnels.Count; i++)
            {
                var face = i + 1;
                TunnelText(() => TunnelSummary(face));
                TunnelButton(() => ExtendText(face), () => ExtendTunnel(face), () => CanExtend(face));
            }
        }

        private int NextFace(WorkerRecord w) => (w.Face + 1) % (Sim.Record.Tunnels.Count + 1);

        /// <summary>Sends a miner on to the next face (shaft → tunnel 1 → … → shaft).</summary>
        public bool SendMiner(string workerId)
        {
            var w = Sim?.Record.Workers.Find(x => x.Id == workerId);
            if (w == null) return false;
            var ok = _root.Services.Sites.AssignWorker(_siteId, workerId, NextFace(w));
            Refresh();
            return ok;
        }

        private string TunnelSummary(int face)
        {
            var sim = Sim;
            var t = sim?.Tunnel(face);
            if (t == null) return "";
            var miners = sim.Record.Workers.FindAll(x => x.Face == face).ConvertAll(x => x.Name);
            var who = miners.Count == 0 ? "nobody digging" : string.Join(", ", miners);
            var pct = t.DurabilityMicro > 0 ? 100.0 * t.DoneMicro / t.DurabilityMicro : 0;
            return $"<b>Level {t.Level} tunnel</b> · depth {t.Depth} layer · {sim.TunnelCellsDug(t)} blocks · " +
                   (t.Status == SiteStatus.AwaitDecision ? "stretch cleared" : $"{pct:0}% of this stretch") + $" · {who}";
        }

        private int Capacity(TunnelRecord t) => int.MaxValue;

        private bool CanExtend(int face)
        {
            var sim = Sim;
            var t = sim?.Tunnel(face);
            return t != null && t.Status == SiteStatus.AwaitDecision && sim.TunnelCellsDug(t) + sim.Config.TunnelStretchCells <= Capacity(t);
        }

        private string ExtendText(int face)
        {
            var sim = Sim;
            var t = sim?.Tunnel(face);
            if (t == null) return "";
            if (t.Status != SiteStatus.AwaitDecision) return "Extend tunnel (when this stretch is cleared)";
            if (!CanExtend(face)) return "This tunnel has reached the end of the charted frontier";
            var now = sim.TunnelDurability(t);
            var next = SiteMath.TunnelDurability(sim.Config, t.Depth, t.K + 1);
            return $"Extend tunnel · effort {now} → {next} per stretch";
        }

        public bool ExtendTunnel(int face)
        {
            if (!CanExtend(face)) return false;
            var ok = _root.Services.Sites.ExtendTunnel(_siteId, face);
            Refresh();
            return ok;
        }

        private void TunnelText(Func<string> provider) => _tunnelTexts.Add((_ui.LiveText(_tunnels, _theme.smallSize), provider));

        private void TunnelButton(Func<string> text, Action onClick, Func<bool> enabled)
        {
            var b = _ui.RowButton(_tunnels, onClick, out var label);
            _tunnelButtons.Add((b, label, text, enabled));
        }

        public void Show(string siteId)
        {
            LastMessage = "";
            _facilitiesKey = "";
            _tunnelsKey = "";
            _siteId = siteId;
            _deeperArmedUntil = -1f;
            _diagramKey = "";
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

        private SiteSimulator Sim => _root.Services.Sites.Get(_siteId);

        // ------------------------------------------------------------------ commands

        private void OnContinue()
        {
            _root.Services.Sites.ChooseContinue(_siteId);
            _deeperArmedUntil = -1f;
            Refresh();
        }

        private void OnDeeper()
        {
            var sim = Sim;
            if (sim == null || !sim.CanGoDeeper) return;
            if (sim.Config.Kind == SiteKind.Mine)
            {
                _root.Services.Sites.ChooseDeeper(_siteId);   // the layer stays open: its tunnels can still be worked
                Refresh();
                return;
            }
            // Leaving a Forest area is permanent (GDD 7), so it takes a second tap to confirm.
            if (Time.unscaledTime > _deeperArmedUntil)
            {
                _deeperArmedUntil = Time.unscaledTime + 3f;
                Refresh();
                return;
            }
            _deeperArmedUntil = -1f;
            _root.Services.Sites.ChooseDeeper(_siteId);
            Refresh();
        }

        private void Cycle(EquipmentSlot slot)
        {
            _root.Services.Sites.DebugCyclePlayerGear(slot);
            Refresh();
        }

        // ------------------------------------------------------------------ view

        private void Refresh()
        {
            var sim = Sim;
            if (sim == null) return;
            var s = sim.Record;
            var mine = sim.Config.Kind == SiteKind.Mine;
            var band = sim.Config.Bands[s.Depth - 1];

            _title.text = mine ? $"Mine · Depth {s.Depth}" : $"Forest · {band.Name}";
            RebuildDiagram(sim);

            var sb = new StringBuilder();
            sb.Append("<b>Current yield:</b> ").Append(CompositionText(s.Composition)).Append('\n');
            if (mine && sim.DigMode)
            {
                var deepest = 0;
                foreach (var d in s.Dug) deepest = Math.Max(deepest, d.Row);
                sb.Append($"<b>Dug:</b> {s.Dug.Count} blocks · deepest row {deepest} (layer {sim.Slab.LayerOfRow(deepest)}) · layers reached {s.Revealed.Count}");
                if (s.FacePending.Count > 0) sb.Append($" · {s.FacePending.Count} waiting where you dug");
            }
            else if (mine)
            {
                var pct = s.SectionDurabilityMicro > 0 ? 100.0 * s.SectionDoneMicro / s.SectionDurabilityMicro : 0;
                var top = s.Depth == 1 ? 2 : sim.FirstLevelOf(s.Depth);
                sb.Append(sim.ShaftDone
                    ? $"<b>Shaft:</b> down through depth {s.Depth} (levels {top}–{top + sim.LevelsPerBand - 1}, a tunnel each)"
                    : $"<b>Shaft:</b> sinking through depth {s.Depth} · {s.SectionEmitted}/{sim.Config.UnitsPerSection} units · {pct:0}% · durability {sim.CurrentDurability}");
                if (s.FacePending.Count > 0) sb.Append($" · {s.FacePending.Count} waiting at the face");
            }
            else if (sim.GroveMode)
            {
                var felled = 0;
                foreach (var kv in s.Chopped) if (sim.IsFelled(kv.Key.Area, kv.Key.Slot)) felled++;
                sb.Append($"<b>Cut:</b> {felled} trees · areas reached {s.Revealed.Count}");
                if (s.FacePending.Count > 0) sb.Append($" · {s.FacePending.Count} logs waiting where you cut");
            }
            else
            {
                var standing = s.Trees.FindAll(t => !t.Felled).Count;
                sb.Append($"<b>Clearing:</b> {standing}/{s.Trees.Count} trees standing · {sim.CurrentDurability} durability each");
                var pending = 0;
                foreach (var t in s.Trees) pending += t.Pending.Count;
                if (pending > 0) sb.Append($" · {pending} waiting at the trees");
            }
            if (!mine && !sim.GroveMode) sb.Append($"\n<b>Effort:</b> ×{SiteMath.ContinuationMultiplier(sim.Config, SiteMath.EffectiveK(sim.Config, s.Depth, s.K)):0.00} (continuation {s.K}) · sections done {s.SectionsCompleted}");
            sb.Append('\n');
            var staging = _root.Services.Logistics.System.State.Containers.TryGetValue(sim.Config.StagingContainerId, out var c) ? c.UnitCount : 0;
            sb.Append($"<b>Staging:</b> {staging}/{sim.Config.StagingCapacity}\n");
            foreach (var w in s.Workers) sb.Append($"<b>{w.Name}:</b> {WorkerView.Describe(sim, w)}\n");
            sb.Append($"<size=80%>Measured: {sim.MeasuredUnitsPerMinute:0.0} units/min to staging (sim time). Sim speed ×{_root.Services.Sites.SpeedMultiplier:0}.</size>");
            if (Feel.DeveloperMode) sb.Append($"\n<size=75%><color=#7A6A5A>DEBUG workers: {sim.BottleneckText}</color></size>");
            _status.text = sb.ToString();

            var awaiting = s.Status == SiteStatus.AwaitDecision;
            if (mine) RefreshMineDecision(sim);
            else if (sim.GroveMode)
            {
                // Cutting is the choice: further left reaches new areas, further back stays in one.
                _continue.gameObject.SetActive(false);
                _deeper.gameObject.SetActive(false);
                _decisionText.text = "<size=85%>Cut any tree you can reach. Further left along the cliff are new areas (new kinds of wood, " +
                                     "tougher trees); further back from the cliff stays in an area and gets a little harder.</size>";
            }
            else
            {
                _decisionText.text = awaiting
                    ? "<b>This clearing is cut.</b> Workers wait until you choose."
                    : "<size=85%>Your next choice comes when this clearing is cut.</size>";
                _continue.gameObject.SetActive(awaiting);
                _deeper.gameObject.SetActive(awaiting);
                if (awaiting)
                {
                    var now = sim.CurrentDurability;
                    var next = sim.ContinueDurability;
                    _continueLabel.text = $"Continue Here · same mix, effort {now} → {next} (+{(next - now) * 100.0 / now:0}%) · same yield";
                    if (sim.CanGoDeeper)
                    {
                        var nb = sim.Config.Bands[s.Depth];
                        var verb = mine ? "Dig Deeper" : "Push Deeper";
                        var where = mine ? $"depth {s.Depth + 1}" : $"area {s.Depth + 1}";
                        _deeperLabel.text = Time.unscaledTime < _deeperArmedUntil
                            ? $"Tap again: leave this {(mine ? "layer" : "area")} for good and reveal {where}"
                            : $"{verb} · reveal {where} ({nb.Discovery}) · composition unknown";
                        _deeper.interactable = true;
                    }
                    else
                    {
                        _deeperLabel.text = mine ? "More depths not yet charted" : "No further areas charted yet";
                        _deeper.interactable = false;
                    }
                }
            }

            RefreshFacilities(sim);
            RefreshTunnels(sim);

            _toolLabel.transform.parent.gameObject.SetActive(Feel.DeveloperMode);   // free gear is a developer tool only
            _packLabel.transform.parent.gameObject.SetActive(Feel.DeveloperMode);
            var gear = _root.Services.Sites;
            _toolLabel.text = $"DEBUG your tool: {UnitVisuals.DisplayName(gear.PlayerToolId)} ({Value(gear.PlayerToolId)}/s) · tap to change";
            _packLabel.text = $"DEBUG your pack: {UnitVisuals.DisplayName(gear.PlayerPackId)} ({Value(gear.PlayerPackId)} units) · tap to change";
        }

        /// <summary>
        /// Mine choices are directions (GDD 6): Continue Here digs left in a layer (a waiting tunnel's next stretch, or the
        /// layer's next level once the shaft is through); Dig Deeper sinks the shaft through the next layer.
        /// </summary>
        private void RefreshMineDecision(SiteSimulator sim)
        {
            var s = sim.Record;
            if (sim.DigMode)
            {
                // Digging is the choice: down reaches the next layer, along the cliff stays in this one.
                _continue.gameObject.SetActive(false);
                _deeper.gameObject.SetActive(false);
                _decisionText.text = "<size=85%>Dig down to reach the next layer (new ores, harder rock). Dig along the cliff to stay in a layer; " +
                                     "it gets a little harder the further you go. Tap shows nearby ore. A shaft two or more blocks deep gets a ladder: climb it with up and down.</size>";
                return;
            }
            var face = sim.WaitingFace();
            var canDeeper = sim.ShaftDone && sim.CanGoDeeper;
            _continue.gameObject.SetActive(face >= 0);
            _deeper.gameObject.SetActive(sim.ShaftDone);
            _decisionText.text = face >= 0
                ? "<b>Miners are waiting for you.</b> Continue left in this layer, or dig deeper. Nothing is decided for you."
                : sim.ShaftDone
                    ? "<size=85%>The shaft is through this layer. Dig deeper whenever you like; tunnels keep working meanwhile.</size>"
                    : "<size=85%>Your next choice comes when the shaft is through this layer or a tunnel stretch is cleared.</size>";
            if (face > 0)
            {
                var t = sim.Tunnel(face);
                var next = SiteMath.TunnelDurability(sim.Config, t.Depth, t.K + 1);
                _continueLabel.text = $"Continue Here · level {t.Level} tunnel one stretch further left · effort {sim.TunnelDurability(t)} → {next} · same yield";
            }
            else if (face == 0)
            {
                var level = sim.NextTunnelLevel(s.Depth);
                _continueLabel.text = level > 0
                    ? $"Continue Here · dig left at level {level} (depth {s.Depth} layer) · effort {SiteMath.TunnelDurability(sim.Config, s.Depth, 1)}"
                    : "Continue Here · join a tunnel in this layer";
            }
            if (sim.ShaftDone)
            {
                if (canDeeper)
                {
                    var nb = sim.Config.Bands[s.Depth];
                    _deeperLabel.text = $"Dig Deeper · sink the shaft {sim.LevelsPerBand} levels to depth {s.Depth + 1} ({nb.Discovery}) · composition unknown";
                }
                else _deeperLabel.text = "More depths not yet charted";
                _deeper.interactable = canDeeper;
            }
        }

        private int Value(string equipmentId) =>
            _root.Services.Catalogue.TryGet<WorkerEquipmentDefinition>(equipmentId, out var e) ? e.Value : 0;

        private static string CompositionText(Composition c)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < c.Weights.Count; i++)
                sb.Append(i > 0 ? ", " : "").Append(UnitVisuals.DisplayName(c.Weights[i].Key)).Append(' ').Append((c.Weights[i].Value * 100).ToString("0")).Append('%');
            if (!string.IsNullOrEmpty(c.RareResourceId) && c.RareChance > 0)
                sb.Append(" · bonus ").Append(UnitVisuals.DisplayName(c.RareResourceId)).Append(' ').Append((c.RareChance * 100).ToString("0.#")).Append("% per unit");
            return sb.ToString();
        }

        private Color MixColor(Composition c)
        {
            var col = Color.black;
            foreach (var w in c.Weights)
                col += (_root.Services.Catalogue.TryGet<ResourceDefinition>(w.Key, out var r) ? r.MapTint : Color.gray) * (float)w.Value;
            col.a = 1f;
            return col;
        }

        private void RebuildDiagram(SiteSimulator sim)
        {
            var s = sim.Record;
            var key = $"{s.Depth}:{s.K}:{s.SectionEmitted}:{s.Status}:{s.Trees.FindAll(t => t.Felled).Count}:{s.Revealed.Count}";
            if (key == _diagramKey) return;
            _diagramKey = key;
            for (var i = _diagram.childCount - 1; i >= 0; i--) Destroy(_diagram.GetChild(i).gameObject);
            if (sim.Config.Kind == SiteKind.Mine) BuildStrata(sim);
            else BuildTrail(sim);
        }

        /// <summary>Vertical cutaway: closed layers compressed above, the current face, the unknown below.</summary>
        private void BuildStrata(SiteSimulator sim)
        {
            var s = sim.Record;
            var rows = new List<(string text, Color color, float height, float progress)>();
            rows.Add(("Surface · Mine mouth", new Color32(140, 190, 96, 255), 50f, -1));
            foreach (var kv in s.Revealed)
            {
                if (kv.Key >= s.Depth) continue;
                rows.Add(($"Depth {kv.Key} · closed · {CompositionText(kv.Value)}", MixColor(kv.Value) * 0.75f, 64f, -1));
            }
            var progress = s.SectionDurabilityMicro > 0 ? (float)s.SectionDoneMicro / s.SectionDurabilityMicro : 0f;
            rows.Add(($"Depth {s.Depth} · ACTIVE FACE · section {s.SectionsCompleted + 1}", MixColor(s.Composition), 170f, progress));
            if (sim.CanGoDeeper)
                rows.Add(($"Depth {s.Depth + 1} · composition unknown · expected: {sim.Config.Bands[s.Depth].Discovery}", new Color32(70, 62, 60, 255), 110f, -1));
            else
                rows.Add(("More depths not yet charted", new Color32(52, 46, 44, 255), 90f, -1));

            var y = 0f;
            foreach (var r in rows)
            {
                var band = _ui.Panel("Band", _diagram, _theme.panelDark, r.color);
                band.type = Image.Type.Sliced;
                band.raycastTarget = false;
                band.rectTransform.anchorMin = new Vector2(0, 1);
                band.rectTransform.anchorMax = new Vector2(1, 1);
                band.rectTransform.pivot = new Vector2(0.5f, 1);
                band.rectTransform.anchoredPosition = new Vector2(0, -y);
                band.rectTransform.sizeDelta = new Vector2(0, r.height - 6f);
                var t = _ui.Text("Label", band.transform, r.text, _theme.smallSize - 6f, _theme.inkOnDark, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                SandboxHud.AutoFit(t, 16f);
                UiFactory.Stretch(t.rectTransform, 16f);
                if (r.progress >= 0f)
                {
                    var bar = _ui.Panel("Progress", band.transform, _theme.chip, new Color(1f, 0.95f, 0.85f, 0.85f));
                    bar.raycastTarget = false;
                    bar.rectTransform.anchorMin = new Vector2(0, 0);
                    bar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(r.progress), 0);
                    bar.rectTransform.pivot = new Vector2(0, 0);
                    bar.rectTransform.sizeDelta = new Vector2(0, 26f);
                    bar.rectTransform.anchoredPosition = new Vector2(0, 10f);
                    t.rectTransform.offsetMin = new Vector2(16f, 40f);
                }
                y += r.height;
            }
            _diagram.GetComponent<LayoutElement>().preferredHeight = y + 8f;
        }

        /// <summary>Horizontal trail: cleared areas behind, the active clearing, the unknown ahead.</summary>
        private void BuildTrail(SiteSimulator sim)
        {
            var s = sim.Record;
            var slots = new List<(string text, Color color, bool current)>();
            foreach (var kv in s.Revealed)
                if (kv.Key < s.Depth) slots.Add(($"Area {kv.Key}\n{(sim.GroveMode ? "reached" : "cleared")}", MixColor(kv.Value) * 0.7f, false));
            var standing = s.Trees.FindAll(t => !t.Felled).Count;
            slots.Add(sim.GroveMode
                ? ($"Area {s.Depth}\n{sim.Config.Bands[s.Depth - 1].Name}\nthe furthest you've cut", MixColor(s.Composition), true)
                : ($"Area {s.Depth}\n{sim.Config.Bands[s.Depth - 1].Name}\n{standing}/{s.Trees.Count} trees", MixColor(s.Composition), true));
            slots.Add(sim.CanGoDeeper
                ? ($"Area {s.Depth + 1}\nunknown\n{sim.Config.Bands[s.Depth].Discovery}", new Color32(60, 74, 62, 255), false)
                : ("No further\nareas charted", new Color32(52, 56, 50, 255), false));

            const float gap = 14f;
            var n = slots.Count;
            for (var i = 0; i < n; i++)
            {
                var chip = _ui.Panel("Area", _diagram, _theme.panelDark, slots[i].color);
                chip.raycastTarget = false;
                chip.rectTransform.anchorMin = new Vector2((float)i / n, 0);
                chip.rectTransform.anchorMax = new Vector2((float)(i + 1) / n, 1);
                chip.rectTransform.offsetMin = new Vector2(gap * 0.5f, slots[i].current ? 0f : 60f);
                chip.rectTransform.offsetMax = new Vector2(-gap * 0.5f, slots[i].current ? 0f : -60f);
                var t = _ui.Text("Label", chip.transform, slots[i].text, _theme.smallSize - 4f, _theme.inkOnDark, TextAlignmentOptions.Center, FontStyles.Bold);
                SandboxHud.AutoFit(t, 16f);
                UiFactory.Stretch(t.rectTransform, 12f);
                if (!slots[i].current) continue;
                // Tree silhouettes: one per tree, dimmed when felled.
                for (var k = 0; k < s.Trees.Count; k++)
                {
                    var tree = _ui.Icon("Tree", chip.transform, _theme.treeIcon, 56f);
                    tree.color = s.Trees[k].Felled ? new Color(1, 1, 1, 0.25f) : Color.white;
                    tree.rectTransform.anchorMin = tree.rectTransform.anchorMax = new Vector2((k + 0.5f) / s.Trees.Count, 0f);
                    tree.rectTransform.anchoredPosition = new Vector2(0, 44f);
                }
            }
            _diagram.GetComponent<LayoutElement>().preferredHeight = 420f;
        }
    }
}
