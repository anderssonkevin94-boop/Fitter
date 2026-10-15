using System.Collections;
using System.Collections.Generic;
using Ravenholm.Domain.Logistics;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// A receiver zone (GDD 3/4): a marked ground decal that accepts compatible carried goods
    /// automatically while the player stands inside it. Before entry, an icon bubble shows what
    /// it accepts. The building gives a small squash response on each delivery.
    /// </summary>
    public sealed class ReceiverView : MonoBehaviour
    {
        private static readonly Dictionary<string, ReceiverView> Registry = new Dictionary<string, ReceiverView>();

        [Tooltip("Stable receiver ID, e.g. 'receiver.smelter'.")]
        [SerializeField] private string receiverId = "";
        [SerializeField] private ReceiverKind kind;
        [SerializeField] private float radius = 0.7f;
        [SerializeField] private SpriteRenderer zoneDecal;
        [SerializeField] private SpriteRenderer bubble;
        [SerializeField] private SpriteRenderer bubbleIcon;
        [Tooltip("Transform squashed on delivery (usually the building sprite).")]
        [SerializeField] private Transform bumpTarget;
        [Tooltip("How far outside the zone the icon bubble appears.")]
        [SerializeField] private float revealDistance = 1.8f;

        private GameRoot _root;
        private Coroutine _bump;
        private Vector3 _bumpBaseScale = Vector3.one;
        private float _bubbleAlpha;

        public string ReceiverId => receiverId;
        public ReceiverKind Kind => kind;
        public float Radius => radius;

        public static bool TryGet(string id, out ReceiverView view) => Registry.TryGetValue(id, out view);

        public void EditorSetup(string id, ReceiverKind k, float r, SpriteRenderer decal, SpriteRenderer bubbleRenderer, SpriteRenderer icon, Transform bump)
        {
            receiverId = id;
            kind = k;
            radius = r;
            zoneDecal = decal;
            bubble = bubbleRenderer;
            bubbleIcon = icon;
            bumpTarget = bump;
        }

        private void OnEnable() => Registry[receiverId] = this;

        private void OnDisable()
        {
            if (Registry.TryGetValue(receiverId, out var v) && v == this) Registry.Remove(receiverId);
            if (_root != null) _root.Services.Logistics.System.Delivered -= OnDelivered;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            var p = transform.position;
            _root.Services.Logistics.RegisterReceiver(receiverId, kind, p.x, p.y, radius);
            _root.Services.Logistics.System.Delivered += OnDelivered;
            if (bumpTarget != null) _bumpBaseScale = bumpTarget.localScale;
            SetBubbleAlpha(0f);
        }

        /// <summary>Tells the logistics where the marker is now (a station moved); a station being built or packed takes nothing.</summary>
        public void Reregister(bool accepting)
        {
            var root = GameRoot.Instance;
            if (root == null) return;
            var p = transform.position;
            root.Services.Logistics.RegisterReceiver(receiverId, kind, p.x, p.y, accepting ? radius : 0f);
        }

        private void Update()
        {
            var controller = SandboxSceneController.Current;
            if (controller == null || controller.Player == null) return;
            var d = Vector2.Distance(controller.Player.position, transform.position);
            var carrying = CarryHasAccepted();
            var target = d <= radius + revealDistance ? (carrying ? 1f : 0.55f) : 0f;
            _bubbleAlpha = Mathf.MoveTowards(_bubbleAlpha, target, Time.deltaTime * 4f);
            SetBubbleAlpha(_bubbleAlpha);
            if (bubble != null) bubble.transform.localPosition = new Vector3(0f, 1.1f + Mathf.Sin(Time.time * 3f) * 0.04f, 0f);

            if (zoneDecal != null)
            {
                var inside = d <= radius;
                var a = inside ? 0.95f : carrying ? 0.75f : 0.4f;
                var c = zoneDecal.color;
                c.a = Mathf.MoveTowards(c.a, a, Time.deltaTime * 3f);
                zoneDecal.color = c;
            }
        }

        public bool CarryHasAccepted()
        {
            var system = _root.Services.Logistics.System;
            foreach (var u in system.State.Carry)
                if (system.Accepts(kind, u)) return true;
            return false;
        }

        private void SetBubbleAlpha(float a)
        {
            if (bubble != null) bubble.color = new Color(1, 1, 1, a);
            if (bubbleIcon != null) bubbleIcon.color = new Color(1, 1, 1, a);
        }

        private void OnDelivered(string id, CarryUnit unit)
        {
            if (id != receiverId || bumpTarget == null) return;
            if (_bump != null) StopCoroutine(_bump);
            _bump = StartCoroutine(Bump());
        }

        private IEnumerator Bump()
        {
            const float duration = 0.16f;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                var k = Mathf.Sin(t / duration * Mathf.PI) * 0.05f;
                bumpTarget.localScale = new Vector3(_bumpBaseScale.x * (1f + k), _bumpBaseScale.y * (1f - k), _bumpBaseScale.z);
                yield return null;
            }
            bumpTarget.localScale = _bumpBaseScale;
            _bump = null;
        }
    }
}
