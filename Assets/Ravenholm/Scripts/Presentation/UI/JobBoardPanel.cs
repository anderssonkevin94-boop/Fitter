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
            title.text = "Job Board";
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
        private ContentCatalogue Cat => _root.Services.Catalogue;

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
            key.Append(s.Posting?.QuestId).Append('|').Append(s.Expeditions.Count).Append('|');
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
            for (var i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            _live.Clear();
            _texts.Clear();
            var s = Board.State;

            Text(() => $"Gold <b>{s.Gold}</b> · Reputation <b>{s.Reputation}</b> · Applicants per job: {Board.ApplicantTarget}");
            Text(() => string.IsNullOrEmpty(_message) ? "" : $"<b>{_message}</b>");

            Header($"Away ({s.Expeditions.Count}/{Board.Config.ActiveSlots})");
            if (s.Expeditions.Count == 0) Text(() => "No expeditions out.");
            foreach (var e in s.Expeditions)
            {
                var exp = e;
                Text(() => ExpeditionLine(exp));
            }

            if (s.Posting != null)
            {
                var p = s.Posting;
                Header($"Posted: {Name(p.QuestId)}");
                if (p.Applicants.Count == 0) Text(() => $"<color=#B44034>{p.NoApplicantReason}</color>");
                foreach (var a in p.Applicants)
                {
                    var app = a;
                    if (Board.HasArrived(app))
                        Button(() => ApplicantCard(app), () => _root.Hud.ShowLoadout(app.ContractorId), null);
                    else
                        Text(() => $"Someone is on the way… ({Math.Max(0, (app.ArriveMs - s.SimMs) / 1000f):0} s)");
                }
                Button(() => "Cancel posting (free)", () => { _root.Services.Jobs.CancelPosting(); _message = "Posting cancelled."; }, null);
            }

            Header("Jobs");
            foreach (var q in Cat.Quests)
            {
                if (q == null) continue;
                var quest = q;
                Text(() => QuestCard(quest));
                Button(() => Board.PostBlockedReason(quest.Id) ?? "Post Job", () => Post(quest.Id), () => Board.PostBlockedReason(quest.Id) == null);
            }
            Button(() => "Close", Close, null);
        }

        // ------------------------------------------------------------------ rows

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

        // ------------------------------------------------------------------ text

        private string Name(string id) => UnitVisuals.DisplayName(id);

        private string QuestCard(QuestDefinition q)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>{q.DisplayName}</b> · difficulty {q.Difficulty} · {q.DurationSeconds:0} s · pays {q.BaseGold} g base");
            if (q.RecoveryContract) sb.Append(" · <b>sponsored: patron pays the fee</b>");
            sb.Append('\n').Append(q.Brief);
            if (q.Threats.Count == 0) sb.Append("\nThreats: none expected");
            else
            {
                sb.Append("\nThreats: ");
                var first = true;
                foreach (var t in q.Threats)
                {
                    sb.Append(first ? "" : ", ").Append(t.knownAtStart ? t.threat.DisplayName : "Threat not yet identified");
                    first = false;
                }
            }
            return sb.ToString();
        }

        private string ApplicantCard(ApplicantRecord a)
        {
            var c = Board.Contractor(a.ContractorId);
            if (c == null) return "Unknown";
            return $"<b>{c.Name}</b> · {ClassName(c)} L{c.Level} · fee {a.Fee} g\n<size=85%>{Strengths(c)} · {History(c)} · tap to meet</size>";
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

        private string ExpeditionLine(ExpeditionRecord e)
        {
            var c = Board.Contractor(e.ContractorId);
            var left = Math.Max(0, (e.EndMs - Board.State.SimMs) / 1000f);
            var tail = e.TestDeparture ? "TEST DEPARTURE: results arrive in Phase 5" : $"{left:0} s left";
            return $"<b>{c?.Name}</b> · {Name(e.QuestId)} · forecast {e.ForecastBand} · {tail}";
        }
    }
}
