using System;
using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Jobs;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Drawn quest map (GDD 14): authored nodes laid out by region (rows) and map tier (columns), lines for
    /// prerequisites, and a state marker on every node, so the state never relies on colour alone:
    /// [+] won, [o] open, [x] locked, [>] the campaign's next job. Reached regions show their jobs; the next
    /// region shows as a silhouette of unnamed nodes; later regions stay hidden. Tapping a node explains it.
    /// </summary>
    public static class QuestMapView
    {
        private static readonly Color Won = new Color32(120, 170, 96, 255);
        private static readonly Color OpenColour = new Color32(232, 186, 92, 255);
        private static readonly Color Locked = new Color32(190, 180, 166, 255);
        private static readonly Color Next = new Color32(236, 120, 72, 255);
        private static readonly Color Line = new Color32(110, 92, 76, 200);

        public sealed class Node
        {
            public QuestDefinition Quest;
            public Vector2 Position;
            public string Marker;
        }

        /// <summary>Builds the map into <paramref name="area"/>; returns the drawn nodes (tests).</summary>
        public static List<Node> Build(RectTransform area, UiFactory ui, ContentCatalogue cat, JobBoardSystem board, Action<QuestDefinition> onTap)
        {
            var nodes = new List<Node>();
            var regions = new List<RegionDefinition>();
            foreach (var r in cat.Regions) if (r != null) regions.Add(r);
            regions.Sort((a, b) => a.Order.CompareTo(b.Order));
            bool Open(RegionDefinition r)
            {
                foreach (var q in cat.Quests) if (q != null && q.Region == r && board.IsUnlocked(q.Id)) return true;
                return false;
            }

            var shown = new List<RegionDefinition>();
            RegionDefinition rumour = null;
            foreach (var r in regions)
            {
                if (Open(r)) shown.Add(r);
                else if (rumour == null) { rumour = r; break; }
            }
            var rows = shown.Count + (rumour != null ? 1 : 0);
            if (rows == 0) return nodes;

            // The layout has not sized the area yet when this runs: use the panel's content width, never a placeholder.
            var parentWidth = area.parent is RectTransform pr ? pr.rect.width : 0f;
            var width = parentWidth > 500f ? parentWidth - 24f : 1000f;
            var maxTier = 0;
            var stack = 1;
            var perCell = new Dictionary<(RegionDefinition, int), int>();
            foreach (var q in cat.Quests)
            {
                if (q == null) continue;
                maxTier = Math.Max(maxTier, q.MapTier);
                perCell.TryGetValue((q.Region, q.MapTier), out var n);
                perCell[(q.Region, q.MapTier)] = n + 1;
                stack = Math.Max(stack, n + 1);
            }
            var rowHeight = 60f + stack * 90f;   // jobs sharing a tier stack vertically, each with its label
            const float rumourHeight = 150f;
            area.sizeDelta = new Vector2(area.sizeDelta.x, shown.Count * rowHeight + (rumour != null ? rumourHeight : 0f));
            float X(int tier) => 60f + (width - 120f) * tier / Math.Max(1, maxTier);
            var objective = board.CampaignObjective();

            var positions = new Dictionary<QuestDefinition, Vector2>();
            for (var row = 0; row < shown.Count; row++)
            {
                var region = shown[row];
                Band(area, ui, region.DisplayName, row, rowHeight, false);
                var perTier = new Dictionary<int, int>();
                foreach (var q in cat.Quests)
                {
                    if (q == null || q.Region != region) continue;
                    perTier.TryGetValue(q.MapTier, out var k);
                    perTier[q.MapTier] = k + 1;
                    var y = -(row * rowHeight + 76f + k * 90f);
                    positions[q] = new Vector2(X(q.MapTier), y);
                }
            }

            // Prerequisite lines first, so nodes draw over them.
            foreach (var kv in positions)
                foreach (var pre in kv.Key.Prerequisites)
                    if (pre != null && positions.TryGetValue(pre, out var from)) Segment(area, from, kv.Value);

            foreach (var kv in positions)
            {
                var q = kv.Key;
                var p = board.ProgressOf(q.Id);
                string marker;
                Color colour;
                if (q.Id == objective) { marker = ">"; colour = Next; }
                else if (p.Successes > 0) { marker = "+"; colour = Won; }
                else if (board.IsUnlocked(q.Id)) { marker = "o"; colour = OpenColour; }
                else { marker = "x"; colour = Locked; }
                Dot(area, ui, kv.Value, colour, marker, Short(q.DisplayName), () => onTap(q));
                nodes.Add(new Node { Quest = q, Position = kv.Value, Marker = marker });
            }

            if (rumour != null)
            {
                BandAt(area, ui, $"{rumour.DisplayName} (unexplored)", shown.Count * rowHeight, rumourHeight, true);
                var count = 0;
                foreach (var q in cat.Quests) if (q != null && q.Region == rumour) count++;
                for (var i = 0; i < Math.Min(count, 6); i++)
                    Dot(area, ui, new Vector2(X(Math.Min(maxTier, 2 + i)), -(shown.Count * rowHeight + rumourHeight * 0.58f)), Locked, "?", "", null);
            }
            return nodes;
        }

        private static string Short(string name) => name.Length <= 12 ? name : name.Substring(0, 11) + "…";

        private static void Band(RectTransform area, UiFactory ui, string title, int row, float h, bool silhouette) =>
            BandAt(area, ui, title, row * h, h, silhouette);

        private static void BandAt(RectTransform area, UiFactory ui, string title, float top, float h, bool silhouette)
        {
            var band = ui.Panel("Region", area, ui.Theme.chip, silhouette ? new Color(0.75f, 0.72f, 0.68f, 0.45f) : new Color(1f, 0.97f, 0.9f, 0.6f));
            band.raycastTarget = false;
            var rt = band.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, h - 10f);
            rt.anchoredPosition = new Vector2(0, -top);
            var t = ui.Text("Title", band.transform, title, ui.Theme.smallSize - 8f, ui.Theme.ink, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            UiFactory.Stretch(t.rectTransform, 10f);
        }

        private static void Segment(RectTransform area, Vector2 a, Vector2 b)
        {
            var go = new GameObject("Link", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(area, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            var d = b - a;
            rt.sizeDelta = new Vector2(d.magnitude, 5f);
            rt.anchoredPosition = a;
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            var img = go.GetComponent<Image>();
            img.color = Line;
            img.raycastTarget = false;
        }

        private static void Dot(RectTransform area, UiFactory ui, Vector2 at, Color colour, string marker, string label, Action onTap)
        {
            var dot = ui.Panel("Node", area, ui.Theme.roundButton, colour);
            dot.type = Image.Type.Simple;
            var rt = dot.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(52f, 52f);
            rt.anchoredPosition = at;
            var m = ui.Text("Marker", dot.transform, marker, 30f, ui.Theme.ink, TextAlignmentOptions.Center, FontStyles.Bold);
            UiFactory.Stretch(m.rectTransform);
            if (onTap != null)
            {
                // Hit area meets the touch-target size even though the dot is small.
                var hit = UiFactory.Rect("Hit", dot.transform);
                hit.sizeDelta = new Vector2(ui.Theme.minTouchTarget, ui.Theme.minTouchTarget);
                hit.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
                hit.gameObject.AddComponent<Button>().onClick.AddListener(() => onTap());
            }
            if (string.IsNullOrEmpty(label)) return;
            var t = ui.Text("Label", dot.transform, label, 20f, ui.Theme.ink, TextAlignmentOptions.Center);
            var lt = t.rectTransform;
            lt.anchorMin = lt.anchorMax = new Vector2(0.5f, 0);
            lt.pivot = new Vector2(0.5f, 1);
            lt.sizeDelta = new Vector2(112f, 30f);
            lt.anchoredPosition = new Vector2(0, -2f);
            t.raycastTarget = false;
            SandboxHud.AutoFit(t, 14f);
        }
    }
}
