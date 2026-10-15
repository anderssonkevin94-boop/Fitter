using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Making camp at the next clearing (the journey, step 4): what happens, the wagon's load against its storage, and
    /// what still stands in the way. Commands go to the camp service; the panel never edits state.
    /// </summary>
    public sealed class MoveOnPanel : MonoBehaviour
    {
        private UiFactory _ui;
        private UiTheme _theme;
        private GameRoot _root;
        private TextMeshProUGUI _title, _info, _message;
        private RectTransform _buttons;
        private readonly List<(TextMeshProUGUI label, Button button, Func<string> text, Func<bool> enabled)> _live =
            new List<(TextMeshProUGUI, Button, Func<string>, Func<bool>)>();

        public bool IsOpen => gameObject.activeSelf;
        public string InfoText => _info != null ? _info.text : "";
        public string Message => _message != null ? _message.text : "";

        public static MoveOnPanel Create(Transform parent, UiFactory ui, GameRoot root)
        {
            var card = ui.Panel("MoveOn", parent, ui.Theme.panel);
            UiFactory.Place(card.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 24f), new Vector2(1060f, 660f));
            var panel = card.gameObject.AddComponent<MoveOnPanel>();
            panel._ui = ui;
            panel._theme = ui.Theme;
            panel._root = root;
            var content = UiFactory.Rect("Content", card.transform);
            UiFactory.Stretch(content, 28f);
            UiFactory.Column(content.gameObject, 10f, new RectOffset(8, 8, 4, 4));
            panel._title = ui.Text("Title", content, "", ui.Theme.bodySize, ui.Theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            panel._title.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;
            panel._info = ui.Text("Info", content, "", ui.Theme.smallSize, ui.Theme.ink, TextAlignmentOptions.TopLeft);
            panel._info.gameObject.AddComponent<LayoutElement>().preferredHeight = 210f;
            panel._message = ui.Text("Message", content, "", ui.Theme.smallSize - 2f, new Color32(180, 64, 52, 255), TextAlignmentOptions.TopLeft, FontStyles.Bold);
            panel._message.gameObject.AddComponent<LayoutElement>().preferredHeight = 90f;
            panel._buttons = UiFactory.Rect("Buttons", content);
            var grid = panel._buttons.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(480f, 104f);
            grid.spacing = new Vector2(16f, 12f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            panel._buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 230f;
            card.gameObject.SetActive(false);
            return panel;
        }

        private Vector2 Player => SandboxSceneController.Current != null ? (Vector2)SandboxSceneController.Current.Player.position : Vector2.zero;

        public void Open()
        {
            gameObject.SetActive(true);
            _message.text = "";
            Rebuild();
        }

        public void Close() => gameObject.SetActive(false);

        private void Update()
        {
            var camp = _root.Services.Camp;
            _info.text = $"Break camp and move up the road to {camp.NextName}: your stations pack into their kits, the shop folds onto " +
                         $"the wagon and the ox pulls it there. What you cleared stays behind.\nThe wagon's load: {camp.CargoLoad} of {camp.CargoCapacity} " +
                         "(Store goods and materials).";
            foreach (var b in _live)
            {
                b.label.text = b.text();
                b.button.interactable = b.enabled == null || b.enabled();
            }
            if (_message.text == "" || _message.text == _lastReason) _message.text = _lastReason = camp.MoveOnBlockedReason(Player) ?? "";
        }

        private string _lastReason = "";

        private void Rebuild()
        {
            var camp = _root.Services.Camp;
            for (var i = _buttons.childCount - 1; i >= 0; i--) Destroy(_buttons.GetChild(i).gameObject);
            _live.Clear();
            _title.text = $"Make camp at {camp.NextName}";
            Button(() => "Make camp here", () =>
            {
                var why = camp.MoveOn(Player);
                if (why == null) Close();
                else _message.text = why;
            }, () => camp.MoveOnBlockedReason(Player) == null);
            Button(() => "Leave the extras behind", () =>
            {
                var n = camp.LeaveExtrasBehind();
                _message.text = n > 0 ? $"Left {n} units behind." : "";
            }, () => camp.CargoLoad > camp.CargoCapacity);
            Button(() => "Not yet", Close);
        }

        private void Button(Func<string> text, Action onClick, Func<bool> enabled = null)
        {
            var b = _ui.Button("Button", _buttons, text(), onClick, out var label);
            Destroy(b.GetComponent<LayoutElement>());
            label.fontSize = _theme.smallSize;
            label.enableAutoSizing = true;
            label.fontSizeMin = 18f;
            label.fontSizeMax = _theme.smallSize;
            _live.Add((label, b, text, enabled));
        }

        /// <summary>For tests: presses the button whose label starts with <paramref name="prefix"/>.</summary>
        public bool Press(string prefix)
        {
            foreach (var b in _live)
                if (b.label.text.StartsWith(prefix, StringComparison.Ordinal) && b.button.interactable) { b.button.onClick.Invoke(); return true; }
            return false;
        }
    }
}
