using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>Shared workshop surfaces, accessible controls and scrolling panels.</summary>
    public sealed class UiFactory
    {
        private readonly UiTheme _theme;

        public UiFactory(UiTheme theme) => _theme = theme;

        public UiTheme Theme => _theme;

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public Image Panel(string name, Transform parent, Sprite sprite, Color? tint = null)
        {
            Image img;
            if (sprite == _theme.panel)
            {
                img = WorkshopUi.Frame(parent);
                img.name = name;
            }
            else if (sprite == _theme.chip || sprite == _theme.panelDark)
            {
                img = WorkshopUi.Surface(name, parent, tint ?? (sprite == _theme.panelDark ? WorkshopUi.Iron : WorkshopUi.Cream));
            }
            else
            {
                img = Rect(name, parent).gameObject.AddComponent<Image>();
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
                img.color = tint ?? Color.white;
            }
            img.raycastTarget = true;
            return img;
        }

        public TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, FontStyles style = FontStyles.Normal)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            rt.gameObject.AddComponent<ScaledText>();   // applies the player's text size from the next frame on
            return t;
        }

        public Image Icon(string name, Transform parent, Sprite sprite, float size)
        {
            var rt = Rect(name, parent);
            rt.sizeDelta = new Vector2(size, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = size;
            le.preferredHeight = le.minHeight = size;
            return img;
        }

        public Button Button(string name, Transform parent, string label, Action onClick, out TextMeshProUGUI labelText, float height = -1f)
        {
            var img = Rect(name, parent).gameObject.AddComponent<Image>();
            img.color = WorkshopUi.Edge;
            var face = WorkshopUi.Fill("Face", img.transform, WorkshopUi.Gold);
            Stretch(face.rectTransform, 3);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = face;
            var colors = btn.colors;
            colors.pressedColor = new Color(0.85f, 0.78f, 0.7f);
            colors.disabledColor = new Color(.65f, .65f, .65f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick());
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height > 0 ? height : _theme.minTouchTarget;
            labelText = Text("Label", img.transform, label, _theme.bodySize, _theme.ink, TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(labelText.rectTransform, 16f);
            labelText.enableAutoSizing = true;
            labelText.fontSizeMin = 25f;
            labelText.fontSizeMax = _theme.bodySize;
            return btn;
        }

        public static VerticalLayoutGroup Column(GameObject go, float spacing, RectOffset padding)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }
    
        /// <summary>Modal layer with a parchment card, a title and a vertically scrolling content column.</summary>
        public RectTransform ModalScroll(Transform parent, string name, out GameObject layer, out TextMeshProUGUI title)
        {
            var l = Rect(name, parent);
            Stretch(l);
            l.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.07f, 0.05f, 0.18f);
            var card = Panel("Card", l, _theme.panel);
            WorkshopUi.Bounds(card.rectTransform, .025f, .055f, .975f, .94f);
            title = Text("Title", card.transform, "", _theme.titleSize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            WorkshopUi.Bounds(title.rectTransform, 0, 1, 1, 1);
            title.rectTransform.offsetMin = new Vector2(56, -150);
            title.rectTransform.offsetMax = new Vector2(-56, -40);
            SandboxHud.AutoFit(title, 32);
            var rule = WorkshopUi.Fill("Rule", card.transform, WorkshopUi.Wood);
            WorkshopUi.Bounds(rule.rectTransform, 0, 1, 1, 1);
            rule.rectTransform.offsetMin = new Vector2(56, -166);
            rule.rectTransform.offsetMax = new Vector2(-56, -162);
            var viewport = Rect("Viewport", card.transform);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(44f, 240f);
            viewport.offsetMax = new Vector2(-44f, -186f);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            Column(content.gameObject, 16f, new RectOffset(12, 12, 8, 24));
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var footer = Rect("Footer", card.transform);
            WorkshopUi.Bounds(footer, 0, 0, 1, 0);
            footer.offsetMin = new Vector2(56, 48);
            footer.offsetMax = new Vector2(-56, 180);
            var row = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 18; row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;
            AddScrollHint(card.transform, scroll, 188);
            layer = l.gameObject;
            return content;
        }

        /// <summary>A text row whose content is re-read on every refresh.</summary>
        public TextMeshProUGUI LiveText(Transform parent, float size)
        {
            var card = Panel("Note", parent, _theme.chip);
            card.raycastTarget = false;
            Column(card.gameObject, 0, new RectOffset(24, 24, 22, 22));
            var t = Text("Text", card.transform, "", size, _theme.ink, TextAlignmentOptions.TopLeft);
            t.richText = true;
            return t;
        }

        public void Report(Transform parent, string text)
        {
            foreach (var section in text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
                LiveText(parent, _theme.smallSize).text = section.Trim();
        }

        /// <summary>Persistent actions outside the scrolling body; rebuilding a screen replaces its action.</summary>
        public Button FooterButton(RectTransform content, string name, string text, Action action)
        {
            var footer = content.parent.parent.Find("Footer");
            var old = footer.Find(name);
            if (old != null)
            {
                var button = old.GetComponent<Button>();
                button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => action());
                old.GetComponentInChildren<TextMeshProUGUI>().text = text;
                old.gameObject.SetActive(true);
                return button;
            }
            return Button(name, footer, text, action, out _);
        }

        public void AddScrollHint(Transform parent, ScrollRect scroll, float bottom)
        {
            var hint = Text("MoreBelow", parent, "Scroll for more", 26, WorkshopUi.Iron, TextAlignmentOptions.Center);
            WorkshopUi.Bounds(hint.rectTransform, 0, 0, 1, 0);
            hint.rectTransform.offsetMin = new Vector2(56, bottom);
            hint.rectTransform.offsetMax = new Vector2(-56, bottom + 42);
            var watch = hint.gameObject.AddComponent<WorkshopScrollHint>();
            watch.Scroll = scroll; watch.Label = hint;
        }

        /// <summary>A full-width button with an auto-sized label.</summary>
        public Button RowButton(Transform parent, Action onClick, out TextMeshProUGUI label)
        {
            var b = Button("Button", parent, "", onClick, out label);
            b.transform.Find("Face").GetComponent<Image>().color = WorkshopUi.Cream;
            label.fontSize = _theme.smallSize;
            label.enableAutoSizing = true;
            label.fontSizeMin = 25f;
            label.fontSizeMax = _theme.smallSize;
            return b;
        }
    }

    public sealed class WorkshopGrid : MonoBehaviour
    {
        public int Columns = 2;
        private void LateUpdate()
        {
            var grid = GetComponent<GridLayoutGroup>();
            var width = ((RectTransform)transform).rect.width;
            grid.cellSize = new Vector2((width - grid.padding.horizontal - grid.spacing.x * (Columns - 1)) / Columns, grid.cellSize.y);
        }
    }

    public sealed class WorkshopScrollHint : MonoBehaviour
    {
        public ScrollRect Scroll;
        public TextMeshProUGUI Label;
        private void LateUpdate()
        {
            if (Scroll == null || Scroll.content == null) return;
            Label.enabled = Scroll.content.rect.height > Scroll.viewport.rect.height + 4;
            Label.text = Scroll.verticalNormalizedPosition < .01f ? "Scroll up for more" : "Scroll for more";
        }
    }
}
