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
    /// Job Board (GDD 11): active expeditions, the open posting with its applicants (or why nobody came),
    /// and the jobs you can post. The player posts a job; contractors quote their own fees.
    /// </summary>
    public sealed class JobBoardPanel : MonoBehaviour
    {
        private UiFactory _ui;
        private UiTheme _theme;
        private GameRoot _root;
        private RectTransform _content;
        private TextMeshProUGUI _title;
        private readonly List<(TextMeshProUGUI label, Button button, Func<string> text, Func<bool> enabled)> _live = new List<(TextMeshProUGUI, Button, Func<string>, Func<bool>)>();
        private readonly List<(TextMeshProUGUI text, Func<string> provider)> _texts = new List<(TextMeshProUGUI, Func<string>)>();
        private string _key = "";
        private float _next;
        private string _message = "";

        public bool IsOpen => gameObject.activeSelf;
        public string Message => _message;

        public static JobBoardPanel Create(Transform parent, UiFactory ui, GameRoot root)
        {
            var content = ui.ModalScroll(parent, "JobBoardLayer", out var layer, out var title);
            var panel = layer.AddComponent<JobBoardPanel>();
            panel._ui = ui;
            panel._theme = ui.Theme;
            panel._root = root;
            panel._content = content;
            panel._title = title;
            title.text = root.Config.Balance != null && root.Config.Balance.castlePostsJobs ? "The Castle's Jobs" : "Job Board";
            layer.SetActive(false);
            return panel;
        }

        public void Show()
        {
            if (!IsOpen)
            {
                gameObject.SetActive(true);
                TouchInputRouter.PushModal();
            }
            _message = "";
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
            Refresh();
        }

        private JobBoardSystem Board => _root.Services.Jobs.Board;
        /// <summary>The castle posts the jobs (2026-10-15): the board lists them and the adventurer waiting at the shop.</summary>
        private bool Castle => Board.Config.CastlePosts;
        private ContentCatalogue Cat => _root.Services.Catalogue;

        /// <summary>Nodes on the drawn map (tests).</summary>
        public List<QuestMapView.Node> MapNodes { get; private set; } = new List<QuestMapView.Node>();

        /// <summary>Tapping a map node explains it at the top of the board.</summary>
        public void ExplainNode(QuestDefinition q)
        {
            var reason = Board.IsUnlocked(q.Id) ? (Board.PostBlockedReason(q.Id) ?? (Castle ? "Open: the castle offers it to adventurers" : "Open: post it below")) : Board.LockReason(q.Id);
            _message = $"{q.DisplayName} · difficulty {q.Difficulty} · {reason}";
            FeedbackAudio.Play(FeedbackAudio.Cue.Tap);
        }

        /// <summary>Posts a job through the service (buttons and tests).</summary>
        public bool Post(string questId)
        {
            var ok = _root.Services.Jobs.Post(questId, out var failure);
            _message = ok ? "Posted. Interested contractors will come to the Store over the next half minute." : failure;
            _key = "";
            Refresh();
            return ok;
        }

        private void Refresh()
        {
            var s = Board.State;
            var key = new StringBuilder();
            key.Append(s.Posting?.QuestId).Append('|').Append(s.Expeditions.Count).Append('|').Append(s.Results.Count).Append('|')
               .Append(Board.UnacknowledgedResults).Append('|').Append(_root.Services.Simulation.LastReport != null).Append('|');
            foreach (var q in Cat.Quests) if (q != null) key.Append(Board.IsUnlocked(q.Id) ? 'U' : 'L');
            if (s.Posting != null) foreach (var a in s.Posting.Applicants) key.Append(Board.HasArrived(a) ? 'A' : 'w');
            if (key.ToString() != _key)
            {
                _key = key.ToString();
                Rebuild();
            }
            foreach (var t in _texts) t.text.text = t.provider();
            foreach (var b in _live)
            {
                b.label.text = b.text();
                b.button.interactable = b.enabled == null || b.enabled();
            }
        }

        private void Rebuild()
        {
            WorkshopUi.Clear(_content);
            _live.Clear();
            _texts.Clear();
            var s = Board.State;

            Text(() => Castle
                ? $"Gold <b>{s.Gold}</b> · Reputation <b>{s.Reputation}</b> · The castle posts these jobs. Adventurers take them and come to your shop to be kitted out; you lend the gear for {Board.Config.ShopSharePercent}% of the castle's reward."
                : $"Gold <b>{s.Gold}</b> · Reputation <b>{s.Reputation}</b> · Applicants per job: {Board.ApplicantTarget}");
            var message = _ui.LiveText(_content, _theme.smallSize);
            _texts.Add((message, () =>
            {
                message.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(_message));
                return $"<b>{_message}</b>";
            }));

            Text(CampaignText);
            var unread = s.Results.FindAll(r => !r.Acknowledged);
            if (unread.Count > 0)
            {
                Header($"Returned ({unread.Count} new)");
                foreach (var r in unread)
                {
                    var res = r;
                    Button(() => $"<b>{res.ContractorName}</b> · {Name(res.QuestId)} · {ResultPanel.OutcomeWord(res.Outcome)} · tap to read",
                        () => _root.Hud.ShowResult(res.ExpeditionId), null);
                }
            }

            Header($"Away ({s.Expeditions.Count}/{Board.Config.ActiveSlots})");
            if (s.Expeditions.Count == 0) Text(() => "No expeditions out.");
            foreach (var e in s.Expeditions)
            {
                var exp = e;
                Text(() => ExpeditionLine(exp));
            }

            if (Castle)
            {
                // The journey, step 5: this camp's summons (answering it, with the way through, lets the company move on).
                var camp = Board.Position.Camp;
                var summons = Board.SummonsFor(camp);
                if (summons != null)
                {
                    Header("The castle's summons");
                    Text(() => Board.SummonsAnswered(camp)
                        ? $"{_root.Services.Camp.Name}: <b>{summons.Name}</b>, answered. The road on is open once you are through to {(_root.Services.Camp.HasNext ? _root.Services.Camp.NextName : "the next camp")}."
                        : Board.IsUnlocked(summons.Id)
                            ? $"{_root.Services.Camp.Name}: <b>{summons.Name}</b>. The castle will send someone for it; succeed at it to be allowed to move on."
                            : $"{_root.Services.Camp.Name}: <b>{summons.Name}</b>. {Board.LockReason(summons.Id)}.");
                }
            }

            if (Castle && s.Posting == null)
            {
                Header("At your shop");
                Text(() => s.Expeditions.Count >= Board.Config.ActiveSlots
                    ? "Nobody is waiting: both expedition slots are in use."
                    : $"Nobody is waiting. The castle sends the next adventurer in about {Board.NextCastleOfferDelay() / 1000f:0} s.");
            }
            if (s.Posting != null)
            {
                var p = s.Posting;
                var isSummons = Board.Quest(p.QuestId) is Domain.Jobs.QuestInfo pq && pq.Summons;
                Header(p.ByCastle ? $"At your shop: an adventurer for {Name(p.QuestId)}" : $"Posted: {Name(p.QuestId)}");
                if (isSummons) Text(() => "<b>The castle's summons.</b> Succeed at it to be allowed to move on.");
                if (p.Applicants.Count == 0) Text(() => $"<color=#B44034>{p.NoApplicantReason}</color>");
                foreach (var a in p.Applicants)
                {
                    var app = a;
                    if (Board.HasArrived(app))
                        Button(() => ApplicantCard(app), () => _root.Hud.ShowLoadout(app.ContractorId), null);
                    else
                        Text(() => $"{(p.ByCastle ? "An adventurer is on the way from the castle" : "Someone is on the way")}… ({Math.Max(0, (app.ArriveMs - s.SimMs) / 1000f):0} s)");
                }
                if (p.ByCastle) Button(() => "Turn them away", () => { _root.Services.Jobs.TurnAway(); _message = "Turned away. The castle will send someone else."; }, null);
                else Button(() => "Cancel posting (free)", () => { _root.Services.Jobs.CancelPosting(); _message = "Posting cancelled."; }, null);
            }

            foreach (var region in Regions())
            {
                var any = false;
                foreach (var q in Cat.Quests)
                {
                    if (q == null || q.Region != region || !Board.IsUnlocked(q.Id)) continue;   // locked jobs appear on the map only
                    if (Castle && !Board.PostedHere(q.Id)) continue;   // a later camp's job: the castle posts it there
                    if (!any) Header(region != null ? $"{(Castle ? "The castle's jobs" : "Jobs")} · {region.DisplayName}" : "Jobs");
                    any = true;
                    var quest = q;
                    Text(() => QuestCard(quest));
                    if (!Castle)
                        Button(() => Board.PostBlockedReason(quest.Id) ?? "Post Job", () => Post(quest.Id), () => Board.PostBlockedReason(quest.Id) == null);
                }
            }
            var away = _root.Services.Simulation.LastReport;
            if (away != null) Button(() => $"While you were away ({AwayReportPanel.Duration(away.ElapsedMs)}): show the summary again", () => _root.Hud.ShowAwayReport(away), null);

            Header("Map");
            var mapArea = UiFactory.Rect("QuestMap", _content);
            MapNodes = QuestMapView.Build(mapArea, _ui, Cat, Board, ExplainNode);
            mapArea.gameObject.AddComponent<LayoutElement>().preferredHeight = mapArea.sizeDelta.y;
            Text(MapText);   // the same map in words (accessible, and lists what unlocks each job)


            // Company roster (GDD 10): everyone who has worked for you, including the fallen.
            var worked = new List<ContractorRecord>();
            foreach (var c in s.Contractors.Values) if (c.Jobs > 0 && !c.Sponsored) worked.Add(c);
            if (worked.Count > 0)
            {
                worked.Sort((a, b) => b.Successes != a.Successes ? b.Successes.CompareTo(a.Successes) : string.CompareOrdinal(a.Name, b.Name));
                Header("Contractors you have hired");
                foreach (var c in worked)
                {
                    var rec = c;
                    Text(() => $"<b>{rec.Name}</b> · {ClassName(rec)} L{rec.Level} · {StatusWord(rec)}\n<size=85%>{FullHistory(rec)}</size>");
                }
            }

            var history = s.Results.FindAll(r => r.Acknowledged);
            if (history.Count > 0)
            {
                Header("History");
                for (var i = history.Count - 1; i >= 0 && i >= history.Count - 8; i--)
                {
                    var res = history[i];
                    Button(() => $"{res.ContractorName} · {Name(res.QuestId)} · {ResultPanel.OutcomeWord(res.Outcome)}", () => _root.Hud.ShowResult(res.ExpeditionId), null);
                }
            }
            _ui.FooterButton(_content, "Close", "Close", Close);
        }

        private List<RegionDefinition> Regions()
        {
            var list = new List<RegionDefinition>();
            foreach (var r in Cat.Regions) if (r != null) list.Add(r);
            list.Sort((a, b) => a.Order.CompareTo(b.Order));
            if (list.Count == 0) list.Add(null);
            return list;
        }

        private bool RegionOpen(RegionDefinition r)
        {
            foreach (var q in Cat.Quests) if (q != null && q.Region == r && Board.IsUnlocked(q.Id)) return true;
            return false;
        }

        /// <summary>Campaign breadcrumb (GDD 14): the next main-chain job and what it needs.</summary>
        public string CampaignText()
        {
            if (Board.CampaignComplete)
                return "<b>Journey complete:</b> the castle's last great job is done, and your company stands at its gates. Contracts, blueprints and better gear remain for as long as you like.";
            var id = Board.CampaignObjective();
            if (id == null) return "";
            var s = Board.State;
            var q = Cat.Get<QuestDefinition>(id);
            var status = Board.IsUnlocked(id)
                ? (Castle && s.Posting != null && s.Posting.QuestId == id ? "an adventurer is coming to your shop for it"
                   : Castle && s.Expeditions.Exists(e => e.QuestId == id) ? "underway"
                   : Castle ? "the castle offers it to the next adventurer who can go"
                   : Board.PostBlockedReason(id) == null ? "ready to post" : Board.PostBlockedReason(id))
                : Board.LockReason(id).Replace("Locked: ", "");
            return $"<b>Next for the company:</b> {q.DisplayName} ({(q.Region != null ? q.Region.DisplayName : "")}) · <size=85%>{status}</size>";
        }

        /// <summary>Quest map (GDD 14): regions in order; reached regions list their jobs, the next one shows as a rumour.</summary>
        public string MapText()
        {
            var sb = new StringBuilder();
            var shownRumour = false;
            foreach (var region in Regions())
            {
                if (region != null && !RegionOpen(region))
                {
                    if (shownRumour) continue;   // only the adjacent region appears, as a silhouette
                    shownRumour = true;
                    sb.Append($"<color=#8A7F70><b>{region.DisplayName}</b> (unexplored): {region.Rumour}</color>\n");
                    continue;
                }
                if (region != null) sb.Append($"<b>{region.DisplayName}</b> <size=85%>· {region.Theme}</size>\n");
                var line = new StringBuilder();
                foreach (var q in Cat.Quests)
                {
                    if (q == null || q.Region != region) continue;
                    var p = Board.ProgressOf(q.Id);
                    if (line.Length > 0) line.Append("  ·  ");
                    if (!Board.IsUnlocked(q.Id)) line.Append($"<color=#8A7F70>{q.DisplayName} (locked: {Board.LockReason(q.Id).Replace("Locked: ", "")})</color>");
                    else if (p.Successes > 0) line.Append($"<b>{q.DisplayName}</b> (won {p.Successes})");
                    else line.Append($"<b>{q.DisplayName}</b>");
                }
                sb.Append("<size=90%>").Append(line).Append("</size>\n");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ rows

        private void Header(string text)
        {
            var t = _ui.Text("Header", _content, text, 36, WorkshopUi.Iron, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 88f;
            SandboxHud.AutoFit(t, 28);
        }

        private void Text(Func<string> provider) => _texts.Add((_ui.LiveText(_content, _theme.smallSize), provider));

        private void Button(Func<string> text, Action onClick, Func<bool> enabled)
        {
            var b = _ui.RowButton(_content, onClick, out var label);
            _live.Add((label, b, text, enabled));
        }

        // ------------------------------------------------------------------ text

        private string Name(string id) => UnitVisuals.DisplayName(id);

        private string QuestCard(QuestDefinition q)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>{q.DisplayName}</b> · difficulty {q.Difficulty} · {q.DurationSeconds:0} s · pays {q.BaseGold} g base");
            if (q.RecoveryContract) sb.Append(" · <b>sponsored: patron pays the fee</b>");
            if (q.Challenge) sb.Append(" · <b>challenge contract</b>");
            if (q.Summons) sb.Append(" · <b>the castle's summons</b>");
            sb.Append('\n').Append(q.Brief);
            var view = Board.Quest(q.Id);
            if (view.Threats.Count == 0) sb.Append("\nThreats: none expected");
            else
            {
                sb.Append("\nThreats: ");
                var first = true;
                foreach (var t in view.Threats)
                {
                    sb.Append(first ? "" : ", ").Append(t.Known ? UnitVisuals.DisplayName(t.Id) : "Threat not yet identified");
                    first = false;
                }
            }
            foreach (var fact in view.KnownFacts) sb.Append($"\n<size=85%>Known: {fact}</size>");
            if (view.RouteBonus > 0) sb.Append($"\n<color=#3B6E2E>Known route: +{view.RouteBonus} readiness here</color>");
            foreach (var prep in view.PreparedThreats) sb.Append($"\n<color=#3B6E2E>Prepared: {UnitVisuals.DisplayName(prep)} dealt with by an earlier expedition</color>");
            var needs = new List<string>();
            if (q.MinContractorLevel > 0) needs.Add($"contractor level {q.MinContractorLevel}+");
            if (q.DispatchReputation > 0) needs.Add($"reputation {q.DispatchReputation} when sent");
            if (q.MinWeaponPower > 0) needs.Add($"weapon Power {q.MinWeaponPower}+");
            if (q.MinArmorProtection > 0) needs.Add($"armor Protection {q.MinArmorProtection}+");
            if (q.RequireCounterPotion) needs.Add("a counter potion");
            if (needs.Count > 0) sb.Append("\n<b>Requires:</b> ").Append(string.Join(", ", needs));
            if (!q.Repeatable) sb.Append("\n<size=85%>One-time expedition</size>");
            var prog = Board.ProgressOf(q.Id);
            if (prog.Attempts > 0) sb.Append($"\n<size=85%>Attempts {prog.Attempts} · successes {prog.Successes}</size>");
            if (prog.Successes == 0 && q.FirstSuccessBlueprint != null) sb.Append($"\n<size=85%>First success: {q.FirstSuccessBlueprint.DisplayName}</size>");
            // GDD 11: inspect known threats and rewards before posting.
            if (q.SuccessMaterials.Count > 0) sb.Append($"\n<size=85%>Success also brings: {Rewards(q.SuccessMaterials)}" +
                                                      (q.GreatBonusMaterials.Count > 0 ? $" (+{Rewards(q.GreatBonusMaterials)} on a great success)" : "") + "</size>");
            if (q.PartialMaterials.Count > 0) sb.Append($"\n<size=85%>Partial success: {Rewards(q.PartialMaterials)}</size>");
            return sb.ToString();
        }

        private string ApplicantCard(ApplicantRecord a)
        {
            var c = Board.Contractor(a.ContractorId);
            if (c == null) return "Unknown";
            var terms = Board.State.Posting != null && Board.State.Posting.ByCastle ? "kit them out" : $"fee {a.Fee} g";
            return $"<b>{c.Name}</b> · {ClassName(c)} L{c.Level} · {terms}\n<size=85%>{Strengths(c)} · {History(c)} · tap to meet</size>";
        }

        private string ClassName(ContractorRecord c) => Cat.TryGet<AdventurerClassDefinition>(c.ClassId, out var d) ? d.DisplayName : c.ClassId;

        /// <summary>Two most relevant strengths (GDD 11): best two stats, plus trait names.</summary>
        public string Strengths(ContractorRecord c)
        {
            var s = Board.BaseStats(c);
            var stats = new List<(string n, int v)> { ("Might", s.Might), ("Survival", s.Survival), ("Insight", s.Insight) };
            stats.Sort((x, y) => y.v.CompareTo(x.v));
            var text = $"{stats[0].n} {stats[0].v}, {stats[1].n} {stats[1].v}";
            foreach (var t in c.Traits) text += " · " + UnitVisuals.DisplayName(t);
            return text;
        }

        public static string History(ContractorRecord c) =>
            c.Jobs == 0 ? "no jobs for you yet" : $"{c.Successes}/{c.Jobs} jobs succeeded{(string.IsNullOrEmpty(c.LastNote) ? "" : " · " + c.LastNote)}";

        /// <summary>GDD 10: latest three jobs plus lifetime totals, injuries, toughest job and discoveries.</summary>
        public static string FullHistory(ContractorRecord c)
        {
            if (c.Jobs == 0) return "No jobs for you yet.";
            var sb = new StringBuilder();
            sb.Append($"{c.Successes} of {c.Jobs} jobs succeeded");
            if (c.Injuries > 0) sb.Append($" · injured {c.Injuries}×");
            if (c.HighestDifficulty > 0) sb.Append($" · toughest success: difficulty {c.HighestDifficulty}");
            if (c.Discoveries.Count > 0) sb.Append(" · discovered: ").Append(string.Join(", ", c.Discoveries.ConvertAll(UnitVisuals.DisplayName)));
            if (c.Recent.Count > 0) sb.Append("\nLatest: ").Append(string.Join("; ", c.Recent));
            if (!string.IsNullOrEmpty(c.DeathNote)) sb.Append($"\n<b>Remembered:</b> {c.DeathNote}");
            return sb.ToString();
        }

        private static string StatusWord(ContractorRecord c) =>
            c.Status == ContractorStatus.Dead ? "fallen" : c.Status == ContractorStatus.Injured ? "recovering" : c.Status == ContractorStatus.OnExpedition ? "away on a job" : "available";

        private static string Rewards(IReadOnlyList<ResourceCost> list)
        {
            var parts = new List<string>();
            foreach (var m in list) if (m.resource != null) parts.Add($"{m.quantity} {m.resource.DisplayName}");
            return string.Join(", ", parts);
        }

        private string ExpeditionLine(ExpeditionRecord e)
        {
            var c = Board.Contractor(e.ContractorId);
            var left = Math.Max(0, (e.EndMs - Board.State.SimMs) / 1000f);
            var tail = e.TestDeparture ? "TEST DEPARTURE (debug; does not resolve)" : $"back in {left:0} s";
            var dev = Feel.DeveloperMode ? $"\n<size=75%><color=#7A6A5A>DEBUG seed {e.Seed} · D {e.Difficulty} · route {e.RouteBonus} · {e.Id}</color></size>" : "";
            return $"<b>{c?.Name}</b> · {Name(e.QuestId)} · forecast {e.ForecastBand} · {tail}{dev}";
        }
    }
}
