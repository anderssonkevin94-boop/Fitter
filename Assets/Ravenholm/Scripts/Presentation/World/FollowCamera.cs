using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Fixed-angle orthographic follow camera, fit to a world WIDTH so every portrait aspect
    /// ratio shows the same horizontal slice of town. No rotation or zoom in the game (locked);
    /// the width index exists only so the debug panel can compare framings.
    /// Phase 1 adds the dead zone and damping.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;

        private static int _viewWidthIndex = -1;
        private Camera _camera;

        public static int ViewWidthIndex
        {
            get => _viewWidthIndex;
            set => _viewWidthIndex = value;
        }

        public void EditorSetup(Transform followTarget) => target = followTarget;

        private void Awake() => _camera = GetComponent<Camera>();

        private void LateUpdate()
        {
            var root = GameRoot.Instance;
            if (root == null || target == null) return;
            var s = root.Config.SandboxSettings;
            if (_viewWidthIndex < 0) _viewWidthIndex = s.defaultViewWidthIndex;
            _viewWidthIndex = Mathf.Clamp(_viewWidthIndex, 0, s.viewWidthOptions.Length - 1);

            var width = s.viewWidthOptions[_viewWidthIndex];
            _camera.orthographicSize = OrthoSizeForWidth(width, _camera.aspect);

            var halfH = _camera.orthographicSize;
            var halfW = halfH * _camera.aspect;
            var pos = (Vector2)target.position + new Vector2(0f, s.cameraLookAheadY);
            var b = s.plotBounds;
            const float margin = 1.5f;   // show a little of the diorama edge
            pos.x = ClampAxis(pos.x, b.xMin - margin, b.xMax + margin, halfW);
            pos.y = ClampAxis(pos.y, b.yMin - margin, b.yMax + margin, halfH);
            transform.position = new Vector3(pos.x, pos.y, -10f);
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
