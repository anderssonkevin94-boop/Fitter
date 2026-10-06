using System.Text;
using Ravenholm.Services;
using TMPro;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// "While you were away" (GDD 15): how long, what the sites deposited and where they stopped,
    /// what the facilities processed, which expeditions returned, who recovered, and the Trader.
    /// Everything shown was already applied by the catch-up; closing only dismisses the card.
    /// </summary>
    public sealed class AwayReportPanel : MonoBehaviour
    {
        private UiFactory _ui;
        private UiTheme _theme;
        private GameRoot _root;
        private RectTransform _content;
        private TextMeshProUGUI _title;

        public bool IsOpen => gameObject.activeSelf;
        public string BodyText { get; private set; } = "";

        public static AwayReportPanel Create(Transform parent, UiFactory ui, GameRoot root)
        {
            var content = ui.ModalScroll(parent, "AwayLayer", out var layer, out var title);
            var panel = layer.AddComponent<AwayReportPanel>();
            panel._ui = ui;
            panel._theme = ui.Theme;
            panel._root = root;
            panel._content = content;
            panel._title = title;
            layer.SetActive(false);
            return panel;
        }

        public void Show(AwayReport report)
        {
            if (report == null) return;
            for (var i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            _title.text = "While you were away";
            BodyText = Describe(report);
            _ui.LiveText(_content, _theme.bodySize - 4f).text = BodyText;
            if (report.Results.Count > 0)
            {
                _ui.RowButton(_content, ViewResults, out var view);
                view.text = report.Results.Count == 1 ? "Read the result" : $"View all {report.Results.Count} results";
            }
            _ui.RowButton(_content, Close, out var label);
            label.text = "Back to work";
            if (!IsOpen)
            {
                gameObject.SetActive(true);
                TouchInputRouter.PushModal();
            }
        }

        public void Close()
        {
            if (!IsOpen) return;
            gameObject.SetActive(false);
            TouchInputRouter.PopModal();
            if (_root.Services.Simulation.PendingReport != null) _root.Services.Simulation.PendingReport = null;
        }

        /// <summary>Dismisses the summary and opens the returned results (GDD 18 "View results").</summary>
        public void ViewResults()
        {
            Close();
            var unread = _root.Services.Jobs.State.Results.FindAll(r => !r.Acknowledged);
            if (unread.Count == 1) _root.Hud.ShowResult(unread[0].ExpeditionId);
            else _root.Hud.ShowJobBoard();
        }

        private void OnDestroy()
        {
            if (IsOpen) TouchInputRouter.PopModal();
        }

        public static string Duration(long ms)
        {
            var minutes = ms / 60000;
            if (minutes < 1) return $"{ms / 1000} seconds";
            if (minutes < 60) return $"{minutes} min";
            return minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
        }

        public static string Describe(AwayReport r)
        {
            var sb = new StringBuilder();
            sb.Append($"You were away for <b>{Duration(r.ElapsedMs)}</b>.");
            if (r.Capped) sb.Append($" Sites and facilities worked for the first {Duration(r.ResourceMs)} (the most they manage unattended).");
            if (r.ClockWentBackwards) sb.Append("\n<color=#B44034>The device clock went backwards; no time was added.</color>");
            sb.Append("\n\n");

            sb.Append("<b>Sites</b>\n");
            if (r.Deposits.Count == 0) sb.Append("No new deposits.\n");
            foreach (var kv in r.Deposits) sb.Append($"{UnitVisuals.DisplayName(kv.Key)}: {kv.Value} raw units waiting at staging\n");
            foreach (var kv in r.SiteBlockers) sb.Append($"<color=#B44034>[!] {UnitVisuals.DisplayName(kv.Key)} stopped: {kv.Value}</color>\n");

            sb.Append("\n<b>Facilities</b>\n");
            if (r.Processed.Count == 0) sb.Append("Nothing processed (they only process what was delivered).\n");
            foreach (var kv in r.Processed) sb.Append($"+{kv.Value} {UnitVisuals.DisplayName(kv.Key)}\n");

            sb.Append("\n<b>Expeditions</b>\n");
            if (r.Results.Count == 0) sb.Append("No expeditions returned.\n");
            // Long lists are grouped (GDD 18): counts by outcome, then the first few lines.
            if (r.Results.Count > 4)
            {
                var counts = new System.Collections.Generic.SortedDictionary<string, int>();
                foreach (var res in r.Results)
                {
                    var w = ResultPanel.OutcomeWord(res.Outcome);
                    counts.TryGetValue(w, out var n);
                    counts[w] = n + 1;
                }
                var parts = new System.Collections.Generic.List<string>();
                foreach (var kv in counts) parts.Add($"{kv.Value} × {kv.Key}");
                sb.Append(string.Join(" · ", parts)).Append('\n');
            }
            var shown = 0;
            foreach (var res in r.Results)
            {
                if (++shown > 4) { sb.Append($"…and {r.Results.Count - 4} more.\n"); break; }
                sb.Append(res.Died ? $"{res.ContractorName} did not return from {UnitVisuals.DisplayName(res.QuestId)}\n"
                                   : $"{res.ContractorName}: {UnitVisuals.DisplayName(res.QuestId)} · {ResultPanel.OutcomeWord(res.Outcome)} · +{res.GoldEarned} gold\n");
            }
            if (r.Results.Count > 0) sb.Append("Open each result from the Job Board.\n");
            if (r.Recovered.Count > 0) sb.Append("Recovered from injury: ").Append(string.Join(", ", r.Recovered)).Append('\n');

            if (!string.IsNullOrEmpty(r.TraderStatus)) sb.Append("\n<b>Trader</b>\n").Append(r.TraderStatus).Append('\n');
            return sb.ToString();
        }
    }
}
