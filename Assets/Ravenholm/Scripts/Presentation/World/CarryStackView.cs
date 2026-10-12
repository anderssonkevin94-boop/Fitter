using System.Collections;
using System.Collections.Generic;
using Ravenholm.Domain.Logistics;
using TMPro;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Visible mixed carry stack (GDD 3): at most 8 representative pieces (newest on top) plus a
    /// count badge. Pickups arc from the pile onto the stack; deliveries arc into the receiver;
    /// pieces a nearby receiver accepts bob to show the match; a full stack pulses once.
    /// Presentation only: the authoritative carry is LogisticsState.Carry.
    /// </summary>
    public sealed class CarryStackView : MonoBehaviour
    {
        public const int MaxVisiblePieces = 8;

        [SerializeField] private Vector2 stackOrigin = new Vector2(0f, 1.04f);
        [SerializeField] private float pieceStep = 0.13f;
        [SerializeField] private float pieceScale = 1.25f;
        [SerializeField] private int baseSortingOrder = 10;
        [SerializeField] private float arcSeconds = 0.2f;
        [Tooltip("Receivers within this distance beyond their zone highlight matching pieces.")]
        [SerializeField] private float highlightRange = 1.8f;

        private GameRoot _root;
        private Transform _stackRoot;
        private readonly List<SpriteRenderer> _pieces = new List<SpriteRenderer>();
        private readonly List<CarryUnit> _shown = new List<CarryUnit>();
        private readonly Stack<SpriteRenderer> _flyerPool = new Stack<SpriteRenderer>();
        private TextMeshPro _badge;
        private int _inFlight;
        private float _pulse;

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            Build();
            var sys = _root.Services.Logistics.System;
            sys.PickedUp += OnPickedUp;
            sys.Delivered += OnDelivered;
            sys.CapacityBlocked += OnFull;
            sys.Changed += Rebuild;
            _root.Services.Logistics.Reloaded += Rebuild;
            Rebuild();
        }

        private void OnDestroy()
        {
            if (_root == null || _root.Services == null) return;
            var sys = _root.Services.Logistics.System;
            sys.PickedUp -= OnPickedUp;
            sys.Delivered -= OnDelivered;
            sys.CapacityBlocked -= OnFull;
            sys.Changed -= Rebuild;
            _root.Services.Logistics.Reloaded -= Rebuild;
        }

        private void Build()
        {
            _stackRoot = new GameObject("CarryStack").transform;
            _stackRoot.SetParent(transform, false);
            _stackRoot.localPosition = stackOrigin;
            _stackRoot.localScale = Vector3.one * pieceScale;
            for (var i = 0; i < MaxVisiblePieces; i++)
            {
                var go = new GameObject($"Piece{i}");
                go.transform.SetParent(_stackRoot, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = baseSortingOrder + i;
                go.SetActive(false);
                _pieces.Add(sr);
            }
            var badge = new GameObject("CountBadge");
            badge.transform.SetParent(_stackRoot, false);
            _badge = badge.AddComponent<TextMeshPro>();
            _badge.fontSize = 2.2f;
            _badge.fontStyle = FontStyles.Bold;
            _badge.alignment = TextAlignmentOptions.Center;
            _badge.outlineColor = new Color32(59, 42, 32, 255);
            _badge.outlineWidth = 0.3f;
            _badge.rectTransform.sizeDelta = new Vector2(1.2f, 0.4f);
            _badge.sortingOrder = baseSortingOrder + MaxVisiblePieces + 1;
        }

        private void Rebuild()
        {
            if (_pieces.Count == 0) return;
            var carry = _root.Services.Logistics.System.State.Carry;
            var first = Mathf.Max(0, carry.Count - MaxVisiblePieces);
            _shown.Clear();
            for (var i = first; i < carry.Count; i++) _shown.Add(carry[i]);
            ApplyVisibility();
            var n = carry.Count;
            _badge.text = n > 0 ? $"{n}/{_root.Services.Logistics.System.Capacity}" : "";
            _badge.transform.localPosition = new Vector3(0f, _shown.Count * pieceStep + 0.16f, 0f);
        }

        private void ApplyVisibility()
        {
            // Pieces still flying in from a pile stay hidden until they land.
            var visible = Mathf.Max(0, _shown.Count - _inFlight);
            for (var i = 0; i < _pieces.Count; i++)
            {
                var on = i < visible;
                _pieces[i].gameObject.SetActive(on);
                if (on) _pieces[i].sprite = UnitVisuals.StackSprite(_shown[i]);
            }
        }

        private SupplierAnimator _animator;
        private bool _behind;

        private void Update()
        {
            if (_pieces.Count == 0) return;
            // With painted animations the pile rests on the outstretched forearms: in front when facing the camera,
            // behind the body when facing away.
            // In other poses (mining, climbing) it rides on the head as before.
            if (_animator == null) _animator = GetComponentInChildren<SupplierAnimator>();
            var onArms = _animator != null && _animator.CarryAnchor.HasValue;
            _stackRoot.localPosition = onArms ? _animator.CarryAnchor.Value : stackOrigin;
            var behind = onArms && _animator.Back;
            if (_behind != behind)
            {
                _behind = behind;
                for (var i = 0; i < _pieces.Count; i++) _pieces[i].sortingOrder = (_behind ? -baseSortingOrder - MaxVisiblePieces : baseSortingOrder) + i;
            }
            // Highlight pieces that the nearest receiver in range would accept.
            var sys = _root.Services.Logistics.System;
            var p = (Vector2)transform.position;
            ReceiverZone near = null;
            var best = float.MaxValue;
            foreach (var r in sys.State.Receivers.Values)
            {
                var d = Vector2.Distance(p, new Vector2(r.X, r.Y)) - r.Radius;
                if (d <= highlightRange && d < best) { best = d; near = r; }
            }
            for (var i = 0; i < _pieces.Count; i++)
            {
                var t = _pieces[i].transform;
                var match = near != null && i < _shown.Count && sys.Accepts(near.Kind, _shown[i]);
                var bob = match && !Feel.ReducedMotion ? Mathf.Abs(Mathf.Sin(Time.time * 7f + i * 0.6f)) * 0.05f : 0f;
                t.localPosition = new Vector3(i % 2 == 0 ? -0.02f : 0.02f, i * pieceStep + bob, 0f);
                t.localScale = Vector3.one * (match ? 1.1f : 1f);
            }

            _pulse = Mathf.MoveTowards(_pulse, 0f, Time.deltaTime * 3f);
            _badge.transform.localScale = Vector3.one * (Feel.ReducedMotion ? 1f : 1f + _pulse * 0.5f);   // colour still flags "full"
            _badge.color = Color.Lerp(new Color32(250, 242, 226, 255), new Color32(255, 140, 110, 255), _pulse);
        }

        private Vector3 StackTop() => _stackRoot.TransformPoint(new Vector3(0f, Mathf.Min(_shown.Count, MaxVisiblePieces) * pieceStep, 0f));

        private void OnPickedUp(string containerId, CarryUnit unit)
        {
            FeedbackAudio.Play(FeedbackAudio.Cue.Pickup);
            if (!WorldContainerView.TryGet(containerId, out var from)) return;
            _inFlight++;
            StartCoroutine(Fly(unit, from.TopPiecePosition, null, () => { _inFlight = Mathf.Max(0, _inFlight - 1); ApplyVisibility(); }));
        }

        private void OnDelivered(string receiverId, CarryUnit unit)
        {
            FeedbackAudio.Play(FeedbackAudio.Cue.Deliver);
            if (!ReceiverView.TryGet(receiverId, out var to)) return;
            StartCoroutine(Fly(unit, StackTop(), to.transform, null));
        }

        private void OnFull(string containerId)
        {
            _pulse = 1f;
            FeedbackAudio.Play(FeedbackAudio.Cue.Full);
            _root.Hud.ShowToast("Carry full");
        }

        /// <summary>Short arc for one unit. Purely visual; the transfer has already committed.</summary>
        private IEnumerator Fly(CarryUnit unit, Vector3 from, Transform toTarget, System.Action landed)
        {
            var sr = _flyerPool.Count > 0 ? _flyerPool.Pop() : CreateFlyer();
            sr.gameObject.SetActive(true);
            sr.sprite = UnitVisuals.StackSprite(unit);
            // Reduced motion: no flying arc; the stack and receiver update in place.
            for (var t = Feel.ReducedMotion ? arcSeconds : 0f; t < arcSeconds; t += Time.deltaTime)
            {
                var k = t / arcSeconds;
                var to = toTarget != null ? toTarget.position + Vector3.up * 0.3f : StackTop();
                var pos = Vector3.Lerp(from, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.45f);
                sr.transform.position = pos;
                sr.transform.localScale = Vector3.one * pieceScale * (toTarget != null ? 1f - k * 0.4f : 1f);
                yield return null;
            }
            sr.gameObject.SetActive(false);
            _flyerPool.Push(sr);
            landed?.Invoke();
        }

        private SpriteRenderer CreateFlyer()
        {
            var go = new GameObject("Flyer");
            go.transform.SetParent(transform.parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = "Overlay";
            sr.sortingOrder = 5;
            return sr;
        }
    }
}
