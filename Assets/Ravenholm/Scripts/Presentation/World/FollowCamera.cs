using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Fixed-angle orthographic follow camera (GDD 3, locked: no rotation or zoom). Fit to a world
    /// WIDTH so every portrait aspect shows the same slice of town; a dead zone and gentle damping
    /// keep it calm; it is clamped to the town. The width index exists only for the debug panel.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;

        private static int _viewWidthIndex = -1;
        private Camera _camera;
        private Vector2 _focus;
        private Vector2 _velocity;
        private float _extraBottom;   // how far below the town the Mine needs the view to reach
        private bool _snapped;

        public static int ViewWidthIndex
        {
            get => _viewWidthIndex;
            set => _viewWidthIndex = value;
        }

        public void EditorSetup(Transform followTarget) => target = followTarget;

        private void Awake() => _camera = GetComponent<Camera>();

        /// <summary>Jump straight to the target (after load, teleport, or framing change).</summary>
        public void Snap() => _snapped = false;

        private void LateUpdate()
        {
            var root = GameRoot.Instance;
            if (root == null || target == null) return;
            var s = root.Config.SandboxSettings;
            if (_viewWidthIndex < 0) _viewWidthIndex = s.defaultViewWidthIndex;
            _viewWidthIndex = Mathf.Clamp(_viewWidthIndex, 0, s.viewWidthOptions.Length - 1);
            _camera.orthographicSize = OrthoSizeForWidth(s.viewWidthOptions[_viewWidthIndex], _camera.aspect);

            var t = (Vector2)target.position + new Vector2(0f, s.cameraLookAheadY);
            // The camera simply follows the player, into the Mine too (GDD 6); "Look down the shaft" frames the working
            // level until the player moves again. The view may reach below the town as deep as anyone can stand.
            var mine = MineDigView.Current;
            _extraBottom = 0f;
            if (mine != null)
            {
                var halfH = _camera.orthographicSize;
                if (mine.ViewingDig) t = mine.DigFocus;
                var lowest = Mathf.Min(mine.DeepestFloorY, mine.DigFocus.y, target.position.y) - 1.5f;
                _extraBottom = Mathf.Max(0f, s.WorldBounds.yMin - (lowest - halfH));
            }
            if (!_snapped)
            {
                _focus = t;
                _velocity = Vector2.zero;
            }
            else
            {
                var dz = s.cameraDeadZone;
                if (t.x > _focus.x + dz.x) _focus.x = t.x - dz.x;
                if (t.x < _focus.x - dz.x) _focus.x = t.x + dz.x;
                if (t.y > _focus.y + dz.y) _focus.y = t.y - dz.y;
                if (t.y < _focus.y - dz.y) _focus.y = t.y + dz.y;
            }

            var desired = Clamp(_focus, s);
            Vector2 pos = _snapped
                ? Vector2.SmoothDamp(transform.position, desired, ref _velocity, s.cameraSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime)
                : desired;
            pos = Clamp(pos, s);   // never show past the margin, even mid-smoothing
            transform.position = new Vector3(pos.x, pos.y, -10f);
            _snapped = true;
        }

        private Vector2 Clamp(Vector2 p, SandboxSettings s)
        {
            var halfH = _camera.orthographicSize;
            var halfW = halfH * _camera.aspect;
            var b = s.WorldBounds;
            const float margin = 1.5f;        // show a little of the diorama edge
            const float bottomMargin = 3.4f;  // the town's ground continues below its front edge as a cliff of layers (GDD 6)
            p.x = ClampAxis(p.x, b.xMin - margin, b.xMax + margin, halfW);
            p.y = ClampAxis(p.y, b.yMin - Mathf.Max(bottomMargin, _extraBottom), b.yMax + margin, halfH);
            return p;
        }

        public static float CurrentViewWidth(SandboxSettings s)
        {
            var i = _viewWidthIndex < 0 ? s.defaultViewWidthIndex : _viewWidthIndex;
            return s.viewWidthOptions[Mathf.Clamp(i, 0, s.viewWidthOptions.Length - 1)];
        }

        public static float OrthoSizeForWidth(float worldWidth, float aspect) => worldWidth / Mathf.Max(0.01f, aspect) * 0.5f;

        private static float ClampAxis(float value, float min, float max, float halfView)
        {
            if (max - min <= halfView * 2f) return (min + max) * 0.5f;
            return Mathf.Clamp(value, min + halfView, max - halfView);
        }
    }
}
