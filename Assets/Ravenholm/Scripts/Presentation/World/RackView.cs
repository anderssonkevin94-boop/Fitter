using System.Collections.Generic;
using Ravenholm.Content;
using TMPro;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Store rack showing a representative subset of stocked items (GDD 4): distinct items, strongest
    /// first, then newest, with a duplicate badge. Inventory itself is never limited by rack slots.
    /// </summary>
    public sealed class RackView : MonoBehaviour
    {
        [SerializeField] private ItemCategory category;
        [SerializeField] private List<Vector2> slots = new List<Vector2>();
        [SerializeField] private float itemScale = 0.9f;

        private GameRoot _root;
        private readonly List<SpriteRenderer> _items = new List<SpriteRenderer>();
        private readonly List<TextMeshPro> _badges = new List<TextMeshPro>();
        private string _key = "";

        public int VisibleCount { get; private set; }

        public void EditorSetup(ItemCategory c, List<Vector2> slotPositions)
        {
            category = c;
            slots = slotPositions;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            foreach (var s in slots)
            {
                var go = new GameObject("Slot");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = s;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 5;
                go.transform.localScale = Vector3.one * itemScale;
                _items.Add(sr);
                var badgeGo = new GameObject("Badge");
                badgeGo.transform.SetParent(go.transform, false);
                badgeGo.transform.localPosition = new Vector3(0.18f, -0.12f, 0f);
                var t = badgeGo.AddComponent<TextMeshPro>();
                t.fontSize = 1.8f;
                t.fontStyle = FontStyles.Bold;
                t.alignment = TextAlignmentOptions.Center;
                t.color = new Color32(250, 242, 226, 255);
                t.outlineColor = new Color32(59, 42, 32, 255);
                t.outlineWidth = 0.3f;
                t.rectTransform.sizeDelta = new Vector2(0.6f, 0.3f);
                t.sortingOrder = 6;
                _badges.Add(t);
                go.SetActive(false);
            }
        }

        private void Update()
        {
            var st = _root.Services.Logistics.System.State;
            var key = st.StoreItems.Count + ":" + (st.StoreItems.Count > 0 ? st.StoreItems[st.StoreItems.Count - 1] : "") + ":" + string.Join(",", st.PinnedDefinitions);
            if (key == _key) return;
            _key = key;

            var list = Representative(_root, d => d.Category == category && !d.SmallDisplay);   // small pieces go on the display tables
            VisibleCount = Mathf.Min(list.Count, _items.Count);
            for (var i = 0; i < _items.Count; i++)
            {
                var on = i < list.Count;
                _items[i].gameObject.SetActive(on);
                if (!on) continue;
                _items[i].sprite = list[i].def.StackSprite;
                _badges[i].text = list[i].count > 1 ? "×" + list[i].count : "";
            }
        }

        /// <summary>
        /// Stocked items a display shows, one entry per item type with its count: pinned first, then strongest, then newest.
        /// </summary>
        public static List<(ItemDefinition def, int count, string newest)> Representative(GameRoot root, System.Func<ItemDefinition, bool> shows)
        {
            var st = root.Services.Logistics.System.State;
            var cat = root.Services.Catalogue;
            var groups = new Dictionary<string, (ItemDefinition def, int count, string newest)>();
            foreach (var inst in st.StoreItems)
            {
                if (!st.ItemDefinitions.TryGetValue(inst, out var defId) || !cat.TryGet<ItemDefinition>(defId, out var def) || !shows(def)) continue;
                groups.TryGetValue(defId, out var g);
                groups[defId] = (def, g.count + 1, string.CompareOrdinal(inst, g.newest ?? "") > 0 ? inst : g.newest);
            }
            var list = new List<(ItemDefinition def, int count, string newest)>(groups.Values);
            list.Sort((a, b) =>
            {
                var pa = st.PinnedDefinitions.Contains(a.def.Id);
                var pb = st.PinnedDefinitions.Contains(b.def.Id);
                if (pa != pb) return pa ? -1 : 1;   // pinned first
                var sa = Strength(a.def);
                var sb = Strength(b.def);
                return sa != sb ? sb.CompareTo(sa) : string.CompareOrdinal(b.newest, a.newest);
            });
            return list;
        }

        private static int Strength(ItemDefinition d) => d.Power + d.Protection + d.PotionStrength + d.CounterStrength;
    }
}
