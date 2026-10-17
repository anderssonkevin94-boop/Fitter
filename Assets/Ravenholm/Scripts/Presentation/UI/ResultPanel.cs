using Ravenholm.Content;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using System.Text;
using Ravenholm.Domain.Jobs;
using TMPro;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Quest result card (GDD 13): outcome, gold, fee already paid, materials, potions used, gear back or
    /// lost, contractor change, reputation change, new knowledge, and the two or three main reasons.
    /// The consequences were already applied when the expedition completed; "Continue" only acknowledges.
    /// </summary>
    public sealed class ResultPanel : MonoBehaviour
    {
        private UiFactory _ui;
        private UiTheme _theme;
        private GameRoot _root;
        private RectTransform _content;
        private TextMeshProUGUI _title;
        private string _expeditionId;

        public bool IsOpen => gameObject.activeSelf;
        public string BodyText { get; private set; } = "";

        public static ResultPanel Create(Transform parent, UiFactory ui, GameRoot root)
        {
            var content = ui.ModalScroll(parent, "ResultLayer", out var layer, out var title);
            var panel = layer.AddComponent<ResultPanel>();
            panel._ui = ui;
            panel._theme = ui.Theme;
            panel._root = root;
            panel._content = content;
            panel._title = title;
            layer.SetActive(false);
            return panel;
        }

        public void Show(string expeditionId)
        {
            var r = _root.Services.Jobs.State.Results.Find(x => x.ExpeditionId == expeditionId);
            if (r == null) return;
            _expeditionId = expeditionId;
            WorkshopUi.Clear(_content);
            _title.text = Headline(r);
            BodyText = Describe(r);
            if (r.BlueprintGranted != null) BlueprintReveal(r.BlueprintGranted);
            _ui.Report(_content, BodyText);
            _ui.FooterButton(_content, "Continue", "Continue", Continue);
            if (!IsOpen)
            {
                gameObject.SetActive(true);
                TouchInputRouter.PushModal();
            }
        }

        /// <summary>
        /// GDD 13/20 blueprint reveal: name, picture, primary benefit and where to craft it. The unlock is
        /// already permanent; closing the card loses nothing.
        /// </summary>
        private void BlueprintReveal(string blueprintId)
        {
            var cat = _root.Services.Catalogue;
            RecipeDefinition recipe = null;
            foreach (var x in cat.Recipes) if (x != null && x.RequiredBlueprint != null && x.RequiredBlueprint.Id == blueprintId) recipe = x;
            if (recipe == null || recipe.OutputItem == null) return;
            var card = _ui.Panel("BlueprintReveal", _content, _theme.chip, new Color(1f, 0.93f, 0.72f, 1f));
            card.gameObject.AddComponent<LayoutElement>().preferredHeight = 200f;
            var icon = _ui.Icon("Icon", card.transform, WorkshopUi.ItemSprite(recipe.OutputItem), 150f);
            UiFactory.Place(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(24f, 0), new Vector2(150f, 150f));
            var text = _ui.Text("Text", card.transform,
                $"<b>New blueprint: {recipe.OutputItem.DisplayName}</b>\n{recipe.OutputItem.StatLine()}\nCraft it at the <b>{recipe.Station.DisplayName}</b>" +
                (recipe.MinFacilityLevel > 1 ? $" (level {recipe.MinFacilityLevel})" : ""), _theme.smallSize, _theme.ink, TextAlignmentOptions.MidlineLeft);
            text.richText = true;
            UiFactory.Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(196f, 12f);
            text.rectTransform.offsetMax = new Vector2(-20f, -12f);
            SandboxHud.AutoFit(text, 20f);
        }

        public void Continue()
        {
            if (!IsOpen) return;
            _root.Services.Jobs.Acknowledge(_expeditionId);
            gameObject.SetActive(false);
            TouchInputRouter.PopModal();
        }

        private void OnDestroy()
        {
            if (IsOpen) TouchInputRouter.PopModal();
        }

        public static string OutcomeWord(Outcome o) =>
            o == Outcome.GreatSuccess ? "Great Success" : o == Outcome.Success ? "Success" : o == Outcome.PartialInjury ? "Partial success, injured"
            : o == Outcome.Failure ? "Failed and returned" : "Did not return";

        private static string Headline(ResultRecord r) =>
            r.Died ? $"{r.ContractorName} did not return" : $"{UnitVisuals.DisplayName(r.QuestId)}: {OutcomeWord(r.Outcome)}";

        public static string Describe(ResultRecord r)
        {
            var sb = new StringBuilder();
            if (r.Died)
                sb.Append($"<b>{r.ContractorName} did not return. Their expedition equipment was lost.</b>\nThey are remembered in the company history.\n\n");
            else sb.Append($"<b>{OutcomeWord(r.Outcome)}</b> · {r.ContractorName}\n\n");
            if (r.Forced) sb.Append("<color=#B44034>DEBUG: forced outcome</color>\n");

            sb.Append($"Gold earned: <b>{r.GoldEarned}</b>");
            sb.Append(r.Sponsored ? " · fee paid by the patron\n" : $" · fee already paid: {r.FeePaid}\n");
            if (r.Materials.Count > 0) sb.Append("Materials: ").Append(Materials(r.Materials)).Append('\n');
            if (r.BlueprintGranted != null) sb.Append($"<b>New blueprint: {UnitVisuals.DisplayName(r.BlueprintGranted)}</b> (details above).\n");
            if (r.SubstituteGranted) sb.Append("Blueprint already known: materials given instead.\n");
            if (r.ConsumedItems.Count > 0) sb.Append("Used: ").Append(Names(r.ConsumedItems)).Append('\n');
            if (r.ReturnedItems.Count > 0) sb.Append("Back in the Store: ").Append(Names(r.ReturnedItems)).Append('\n');
            if (r.LostItems.Count > 0) sb.Append("<color=#B44034>Lost: ").Append(Names(r.LostItems)).Append("</color>\n");
            var rep = r.ReputationDeltaTenths / 10.0;
            sb.Append($"Reputation: {(rep >= 0 ? "+" : "")}{rep:0.#}\n");
            if (r.LeveledUp) sb.Append($"{r.ContractorName} reached level {r.NewLevel}.\n");
            if (r.Injured) sb.Append($"{r.ContractorName} is injured and needs about 10 minutes to recover.\n");
            if (r.RevealedThreats.Count > 0) sb.Append("Learned: ").Append(Names(r.RevealedThreats)).Append('\n');
            var content = GameRoot.Instance != null ? GameRoot.Instance.Services.Jobs.Content : null;
            var q = content?.Quest(r.QuestId);
            var success = r.Outcome == Outcome.Success || r.Outcome == Outcome.GreatSuccess;
            if (q != null && success && q.Finale)
                sb.Append("<b>The castle's last great job is done. Journey complete!</b> A trophy now stands at your camp. Repeatable contracts, remaining blueprints and better gear are still there for you.\n");
            if (q != null && success)
                foreach (var prep in q.Prepares)
                    sb.Append($"<b>Preparation secured:</b> {UnitVisuals.DisplayName(prep.Value)} will no longer stand in the way of {UnitVisuals.DisplayName(prep.Key)}.\n");
            foreach (var fact in r.Facts) sb.Append("<b>Learned:</b> ").Append(fact).Append('\n');
            if (r.QuestsUnlocked.Count > 0) sb.Append("<b>New jobs on the map:</b> ").Append(Names(r.QuestsUnlocked)).Append('\n');
            if (r.Contributors.Count > 0)
            {
                sb.Append("\n<b>Why</b>\n");
                foreach (var c in r.Contributors) sb.Append("• ").Append(UnitVisuals.PrettyIds(c)).Append('\n');
            }
            return sb.ToString();
        }

        private static string Names(List<string> ids) => string.Join(", ", ids.ConvertAll(UnitVisuals.DisplayName));

        private static string Materials(List<string> entries)
        {
            var parts = new List<string>();
            foreach (var e in entries)
            {
                var i = e.LastIndexOf(':');
                parts.Add(i > 0 ? $"{e.Substring(i + 1)} {UnitVisuals.DisplayName(e.Substring(0, i))}" : e);
            }
            return string.Join(", ", parts);
        }
    }
}
