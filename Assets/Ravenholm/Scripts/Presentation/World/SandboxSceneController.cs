using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Scene-side bridge for the town sandbox: feeds the player position to the logistics
    /// simulation, turns taps into inspections, and picks the context-button target.
    /// Holds no game state.
    /// </summary>
    public sealed class SandboxSceneController : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Transform player;
        [Tooltip("Where 'Unstuck' puts the player (in front of the Store).")]
        [SerializeField] private Vector2 safeSpot = new Vector2(0f, 0.6f);
        [Tooltip("Context button reach in world units.")]
        [SerializeField] private float contextRange = 1.8f;
        [Tooltip("Hysteresis so the context label does not flicker between two targets.")]
        [SerializeField] private float contextHysteresis = 0.25f;

        private static readonly List<Collider2D> Hits = new List<Collider2D>();
        private Inspectable[] _inspectables;
        private Inspectable _contextTarget;
        private GameRoot _root;

        public static SandboxSceneController Current { get; private set; }
        public Camera WorldCamera => worldCamera;
        public Transform Player => player;
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
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _inspectables = FindObjectsByType<Inspectable>(FindObjectsInactive.Exclude);
            _root.InputRouter.Tapped += OnTapped;
            _root.Hud.AttachWorldCamera(worldCamera);
        }

        private void OnDestroy()
        {
            if (_root != null && _root.InputRouter != null) _root.InputRouter.Tapped -= OnTapped;
        }

        private void Update()
        {
            if (player == null || _root == null) return;

            // Simulation keeps running during menus (GDD 2); only direct movement stops under a modal.
            _root.Services.Sites.Tick(Time.deltaTime);
            _root.Services.Production.Tick(Time.deltaTime);
            // Pickup and delivery are automatic and never depend on the context button (GDD 3).
            _root.Services.Logistics.System.Tick(Time.deltaTime, player.position.x, player.position.y);

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
            if (_root == null || player == null) return;
            var s = _root.Services.Sandbox.State;
            player.position = new Vector3(s.PlayerX, s.PlayerY, 0f);
            SnapCamera();
        }

        /// <summary>Debug "unstuck": moves the player to a safe spot. Carried goods stay carried.</summary>
        public void Unstuck()
        {
            var motor = player != null ? player.GetComponent<PlayerMotor>() : null;
            if (motor != null) motor.Teleport(safeSpot);
            SnapCamera();
        }

        private void SnapCamera()
        {
            var cam = worldCamera != null ? worldCamera.GetComponent<FollowCamera>() : null;
            if (cam != null) cam.Snap();
        }

        public void Inspect(Inspectable target)
        {
            if (target == null) return;
            target.OnInspected();
            if (!target.OpensOwnPanel) _root.Hud.ShowInfo(target);
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
                if (!Hits[k].isTrigger) continue;   // footprints are not tap targets
                var insp = Hits[k].GetComponent<Inspectable>();
                if (insp == null) continue;
                if (front == null || insp.transform.position.y < front.transform.position.y) front = insp;
            }
            if (front != null) Inspect(front);
        }
    }
}
