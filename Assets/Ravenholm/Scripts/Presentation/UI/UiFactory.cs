using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>Small helpers for building placeholder uGUI from the theme in code.</summary>
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
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = tint ?? Color.white;
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
            var img = Panel(name, parent, _theme.button);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.pressedColor = new Color(0.85f, 0.78f, 0.7f);
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick());
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height > 0 ? height : _theme.minTouchTarget;
            labelText = Text("Label", img.transform, label, _theme.bodySize, _theme.ink, TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(labelText.rectTransform, 12f);
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
            l.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.07f, 0.05f, 0.4f);
            var card = Panel("Card", l, _theme.panel);
            Stretch(card.rectTransform, 28f);
            title = Text("Title", card.transform, "", _theme.titleSize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(48f, -36f), new Vector2(980f, 80f));
            var viewport = Rect("Viewport", card.transform);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(36f, 36f);
            viewport.offsetMax = new Vector2(-36f, -130f);
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
            layer = l.gameObject;
            return content;
        }

        /// <summary>A text row whose content is re-read on every refresh.</summary>
        public TextMeshProUGUI LiveText(Transform parent, float size)
        {
            var t = Text("Text", parent, "", size, _theme.ink, TextAlignmentOptions.TopLeft);
            t.richText = true;
            return t;
        }

        /// <summary>A full-width button with an auto-sized label.</summary>
        public Button RowButton(Transform parent, Action onClick, out TextMeshProUGUI label)
        {
            var b = Button("Button", parent, "", onClick, out label);
            label.fontSize = _theme.smallSize;
            label.enableAutoSizing = true;
            label.fontSizeMin = 20f;
            label.fontSizeMax = _theme.smallSize;
            return b;
        }
    }
}
