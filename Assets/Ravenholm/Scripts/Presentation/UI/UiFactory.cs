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
    }
}
