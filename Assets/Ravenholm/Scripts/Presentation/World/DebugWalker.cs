using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// PHASE 0 FIXTURE. Moves the supplier with the input router's vector so scale and framing
    /// can be judged. Deliberately has NO collisions, damping or obstruction handling; the
    /// Phase 1 kinematic motor replaces it. Position is reported to the sandbox session so it
    /// demonstrates debounced saving.
    /// </summary>
    public sealed class DebugWalker : MonoBehaviour
    {
        [Tooltip("Child that holds the sprites; bobs and flips without moving the sort pivot.")]
        [SerializeField] private Transform visual;

        private GameRoot _root;
        private float _bobPhase;
        private Vector3 _visualBase;

        public void EditorSetup(Transform visualRoot) => visual = visualRoot;

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            if (visual != null) _visualBase = visual.localPosition;

            var s = _root.Services.Sandbox.State;
            transform.position = new Vector3(s.PlayerX, s.PlayerY, 0f);
        }

        private void Update()
        {
            var settings = _root.Config.SandboxSettings;
            var move = _root.InputRouter.MoveVector;
            if (move.sqrMagnitude > 0.0001f)
            {
                var p = (Vector2)transform.position + move * (settings.walkSpeed * Time.deltaTime);
                var b = settings.plotBounds;
                p.x = Mathf.Clamp(p.x, b.xMin + 0.5f, b.xMax - 0.5f);
                p.y = Mathf.Clamp(p.y, b.yMin + 0.5f, b.yMax - 0.5f);
                transform.position = new Vector3(p.x, p.y, 0f);
                _root.Services.Sandbox.ReportPlayerPosition(p.x, p.y);
                _bobPhase += Time.deltaTime * 14f * move.magnitude;
                if (visual != null && Mathf.Abs(move.x) > 0.15f)
                {
                    var sc = visual.localScale;
                    sc.x = Mathf.Abs(sc.x) * (move.x < 0 ? -1f : 1f);
                    visual.localScale = sc;
                }
            }
            else
            {
                _bobPhase = Mathf.MoveTowards(_bobPhase, Mathf.Round(_bobPhase / Mathf.PI) * Mathf.PI, Time.deltaTime * 10f);
            }

            if (visual != null)
                visual.localPosition = _visualBase + new Vector3(0f, Mathf.Abs(Mathf.Sin(_bobPhase)) * 0.06f, 0f);
        }
    }
}
