using Ravenholm.Content;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The first-hour prompt (GDD 19): a small dismissible card under the top bar. It never blocks movement,
    /// sits beneath every panel, and hides itself while a panel is open.
    /// </summary>
    public sealed class TipCard : MonoBehaviour
    {
        private GameRoot _root;
        private TextMeshProUGUI _text;
        private CanvasGroup _group;
        private string _shownId;
        private float _nextCheck;

        public string CurrentText => _group != null && _group.alpha > 0.5f ? _text.text : "";
        public string CurrentStepId => _shownId;

        public static TipCard Create(Transform parent, UiFactory ui, GameRoot root)
        {
            var theme = ui.Theme;
            var card = ui.Panel("TipCard", parent, theme.panel);
            UiFactory.Place(card.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -268f), new Vector2(1060f, 350f));
            var tip = card.gameObject.AddComponent<TipCard>();
            tip._root = root;
            tip._group = card.gameObject.AddComponent<CanvasGroup>();

            tip._text = ui.Text("Text", card.transform, "", theme.smallSize, theme.ink, TextAlignmentOptions.TopLeft);
            tip._text.richText = true;
            tip._text.enableAutoSizing = true;
            tip._text.fontSizeMin = 22f;
            tip._text.fontSizeMax = theme.smallSize;
            var tr = tip._text.rectTransform;
            UiFactory.Stretch(tr);
            tr.offsetMin = new Vector2(44f, 168f);
            tr.offsetMax = new Vector2(-44f, -40f);

            var got = ui.Button("GotIt", card.transform, "Got it", () => tip.Acknowledge(), out var gl, 132f);
            gl.fontSize = theme.smallSize;
            UiFactory.Place((RectTransform)got.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40f, 28f), new Vector2(240f, 132f));
            var hide = ui.Button("HideTips", card.transform, "Hide tips", () => tip.HideAll(), out var hl, 132f);
            hl.fontSize = theme.smallSize;
            UiFactory.Place((RectTransform)hide.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-298f, 28f), new Vector2(260f, 132f));
            tip._group.alpha = 0f;
            tip._group.blocksRaycasts = false;
            return tip;
        }

        public void Acknowledge()
        {
            if (_shownId != null) _root.Services.Tutorial.Acknowledge(_shownId);
            FeedbackAudio.Play(FeedbackAudio.Cue.Tap);
            _nextCheck = 0f;
        }

        public void HideAll()
        {
            _root.Services.Tutorial.SetHidden(true);   // per company; Settings can show them again
            _nextCheck = 0f;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.3f;
            var tutorial = _root.Services.Tutorial;
            tutorial.Refresh();
            var step = tutorial.Current;
            var visible = step != null && !TouchInputRouter.IsBlocked;
            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = visible;
            _group.interactable = visible;
            _shownId = step?.Id;
            if (step != null) _text.text = $"<b>Tip</b> · {step.Prompt}";
        }
    }
}
