using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// PHASE 0 FIXTURE. Shows a static mixed carry stack (at most 8 representative pieces plus
    /// a count badge, GDD 3) so stack silhouettes can be judged at play scale. Owns no
    /// inventory; Phase 1 drives the same view from real carried quantities.
    /// </summary>
    public sealed class CarryStackFixture : MonoBehaviour
    {
        public const int MaxVisiblePieces = 8;

        private static bool _visible = true;
        private static event Action<bool> VisibilityChanged;

        [SerializeField] private List<Sprite> pieces = new List<Sprite>();
        [Tooltip("Units represented, including hidden ones (count badge).")]
        [SerializeField] private int representedUnits = 10;
        [SerializeField] private int capacity = 12;
        [SerializeField] private float pieceStep = 0.13f;
        [SerializeField] private Vector2 stackOrigin = new Vector2(0f, 1.04f);
        [SerializeField] private float pieceScale = 1.25f;
        [SerializeField] private int baseSortingOrder = 10;

        private GameObject _root;

        public static bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                VisibilityChanged?.Invoke(value);
            }
        }

        public void EditorSetup(List<Sprite> stackPieces, int units, int cap)
        {
            pieces = stackPieces;
            representedUnits = units;
            capacity = cap;
        }

        private void Start() => Build();

        private void OnEnable()
        {
            VisibilityChanged += Apply;
            Apply(_visible);
        }

        private void OnDisable() => VisibilityChanged -= Apply;

        private void Apply(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
        }

        private void Build()
        {
            _root = new GameObject("CarryStack");
            _root.transform.SetParent(transform, false);
            _root.transform.localPosition = stackOrigin;
            _root.transform.localScale = Vector3.one * pieceScale;

            var shown = Mathf.Min(MaxVisiblePieces, pieces.Count, representedUnits);
            for (var i = 0; i < shown; i++)
            {
                var go = new GameObject($"Piece{i}");
                go.transform.SetParent(_root.transform, false);
                go.transform.localPosition = new Vector3((i % 2 == 0 ? -0.02f : 0.02f), i * pieceStep, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = pieces[i];
                sr.sortingOrder = baseSortingOrder + i;
            }

            var badge = new GameObject("CountBadge");
            badge.transform.SetParent(_root.transform, false);
            badge.transform.localPosition = new Vector3(0f, shown * pieceStep + 0.16f, 0f);
            var text = badge.AddComponent<TextMeshPro>();
            text.text = $"{representedUnits}/{capacity}";
            text.fontSize = 2.2f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color32(250, 242, 226, 255);
            text.outlineColor = new Color32(59, 42, 32, 255);
            text.outlineWidth = 0.3f;
            text.rectTransform.sizeDelta = new Vector2(1.2f, 0.4f);
            text.sortingOrder = baseSortingOrder + MaxVisiblePieces + 1;
            Apply(_visible);
        }
    }
}
