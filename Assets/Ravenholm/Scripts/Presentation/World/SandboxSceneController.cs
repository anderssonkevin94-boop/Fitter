using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Scene-side bridge for the DebugSandbox: turns taps into inspections and finds the
    /// nearest inspectable for the context button. Holds no game state.
    /// </summary>
    public sealed class SandboxSceneController : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Transform player;
        [Tooltip("Context button reach in world units.")]
        [SerializeField] private float contextRange = 1.8f;
        [Tooltip("Hysteresis so the context label does not flicker between two targets.")]
        [SerializeField] private float contextHysteresis = 0.25f;

        private static readonly List<Collider2D> Hits = new List<Collider2D>();
        private Inspectable[] _inspectables;
        private Inspectable _contextTarget;

        public static SandboxSceneController Current { get; private set; }
        public Camera WorldCamera => worldCamera;
        public Inspectable ContextTarget => _contextTarget;

        public void EditorSetup(Camera cam, Transform playerTransform)
        {
            worldCamera = cam;
            player = playerTransform;
        }

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this) Current = null;
        }

        private void Start()
        {
            var root = GameRoot.Instance;
            if (root == null) { enabled = false; return; }
            _inspectables = FindObjectsByType<Inspectable>(FindObjectsInactive.Exclude);
            root.InputRouter.Tapped += OnTapped;
            root.Hud.AttachWorldCamera(worldCamera);
        }

        private void OnDestroy()
        {
            var root = GameRoot.Instance;
            if (root != null && root.InputRouter != null) root.InputRouter.Tapped -= OnTapped;
        }

        private void Update()
        {
            if (player == null || _inspectables == null) return;
            Inspectable best = null;
            var bestDist = float.MaxValue;
            foreach (var i in _inspectables)
            {
                if (i == null || i.transform == player) continue;   // the player is tappable but never a context target
                var d = Vector2.Distance(player.position, i.transform.position);
                if (i == _contextTarget) d -= contextHysteresis;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            _contextTarget = bestDist <= contextRange ? best : null;
        }

        /// <summary>Moves the player view to the saved position (after a debug reload or reset).</summary>
        public void SnapPlayerToSavedPosition()
        {
            var root = GameRoot.Instance;
            if (root == null || player == null) return;
            var s = root.Services.Sandbox.State;
            player.position = new Vector3(s.PlayerX, s.PlayerY, 0f);
        }

        public void Inspect(Inspectable target)
        {
            if (target == null) return;
            target.OnInspected();
            GameRoot.Instance.Hud.ShowInfo(target);
        }

        private void OnTapped(Vector2 screenPos)
        {
            if (worldCamera == null) return;
            var world = worldCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -worldCamera.transform.position.z));
            var filter = new ContactFilter2D { useTriggers = true };
            Physics2D.OverlapPoint(world, filter, Hits);

            // Frontmost = lowest pivot y (sprites sort by feet position).
            Inspectable front = null;
            for (var k = 0; k < Hits.Count; k++)
            {
                var insp = Hits[k].GetComponent<Inspectable>();
                if (insp == null) continue;
                if (front == null || insp.transform.position.y < front.transform.position.y) front = insp;
            }
            if (front != null) Inspect(front);
        }
    }
}
