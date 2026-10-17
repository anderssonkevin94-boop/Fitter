using System;
using System.Collections.Generic;
using Ravenholm.Content;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>Restrained timber-and-iron UI built from scalable uGUI elements.</summary>
    public static class WorkshopUi
    {
        public static readonly Color Cream = new Color32(244, 232, 207, 255);
        public static readonly Color Wood = new Color32(164, 111, 62, 255);
        public static readonly Color Edge = new Color32(85, 58, 38, 255);
        public static readonly Color Gold = new Color32(232, 181, 78, 255);
        public static readonly Color Iron = new Color32(77, 101, 119, 255);

        private static readonly Dictionary<ItemDefinition, Sprite> ItemSprites = new Dictionary<ItemDefinition, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSprites()
        {
            foreach (var sprite in ItemSprites.Values) if (sprite != null) UnityEngine.Object.Destroy(sprite);
            ItemSprites.Clear();
        }

        /// <summary>Use the authored display artwork, cropped to its authored visible bounds.</summary>
        public static Sprite ItemSprite(ItemDefinition item)
        {
            if (item == null) return null;
            if (ItemSprites.TryGetValue(item, out var cached)) return cached;
            var source = item.DisplaySprite(false, out var bounds);
            if (source == null || bounds.width <= 0 || bounds.height <= 0) return item.Icon;
            var rect = source.rect;
            var crop = new Rect(rect.x + bounds.x, rect.y + rect.height - bounds.y - bounds.height, bounds.width, bounds.height);
            if (crop.xMin < rect.xMin || crop.yMin < rect.yMin || crop.xMax > rect.xMax || crop.yMax > rect.yMax) return item.Icon;
            var sprite = Sprite.Create(source.texture, crop, new Vector2(.5f, .5f), source.pixelsPerUnit);
            sprite.name = item.Id + "_UI"; sprite.hideFlags = HideFlags.HideAndDontSave;
            ItemSprites[item] = sprite;
            return sprite;
        }

        public static Image Fill(string name, Transform parent, Color color)
        {
            var rt = UiFactory.Rect(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return image;
        }

        public static void Bounds(RectTransform rt, float x0, float y0, float x1, float y1, float inset = 0)
        {
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }

        public static Image Surface(string name, Transform parent, Color color)
        {
            var edge = UiFactory.Rect(name, parent).gameObject.AddComponent<Image>();
            edge.color = Edge; edge.raycastTarget = false;
            var face = Fill("Face", edge.transform, color);
            UiFactory.Stretch(face.rectTransform, 3);
            return edge;
        }

        public static Image Frame(Transform parent)
        {
            var card = Surface("Card", parent, Wood);
            var topEdge = Fill("TimberHighlight", card.transform, new Color32(204, 153, 93, 255));
            Bounds(topEdge.rectTransform, 0, 1, 1, 1);
            topEdge.rectTransform.offsetMin = new Vector2(4, -8); topEdge.rectTransform.offsetMax = new Vector2(-4, -3);
            var paper = Surface("Paper", card.transform, Cream);
            paper.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UiFactory.Stretch(paper.rectTransform, 24);
            foreach (var corner in new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one })
            {
                var bracket = Surface("IronCorner", card.transform, Iron);
                bracket.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                UiFactory.Place(bracket.rectTransform, corner, corner, Vector2.zero, new Vector2(52, 52));
                var rivet = Fill("Rivet", bracket.transform, new Color32(153, 171, 181, 255));
                UiFactory.Place(rivet.rectTransform, Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, new Vector2(9, 9));
            }
            return card;
        }

        public static TextMeshProUGUI Label(UiFactory ui, Transform parent, string text, float size,
            float x0, float y0, float x1, float y1, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var label = ui.Text("Label", parent, text, size, ui.Theme.ink, align, bold ? FontStyles.Bold : FontStyles.Normal);
            Bounds(label.rectTransform, x0, y0, x1, y1, 8);
            label.enableAutoSizing = true; label.fontSizeMin = size * .8f; label.fontSizeMax = size;
            return label;
        }

        public static Button Button(UiFactory ui, string name, Transform parent, string text, Action action,
            out TextMeshProUGUI label, bool primary = true)
        {
            var button = ui.Button(name, parent, text, action, out label);
            var face = button.transform.Find("Face").GetComponent<Image>();
            face.color = primary ? Gold : Cream;
            label.fontSize = 34; label.enableAutoSizing = true; label.fontSizeMin = 25; label.fontSizeMax = 34;
            return button;
        }

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false); UnityEngine.Object.Destroy(child);
            }
        }
    }
}
