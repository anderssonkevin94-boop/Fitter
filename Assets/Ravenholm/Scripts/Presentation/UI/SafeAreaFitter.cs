using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>Keeps its RectTransform inside the device safe area (notch, Dynamic Island, home bar).</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        /// <summary>Normalized safe-area override used by screenshot captures and tests.</summary>
        public static Rect? NormalizedOverride;

        private Rect _applied = new Rect(-1, -1, -1, -1);

        private void Update() => Apply();

        public void Apply()
        {
            var n = NormalizedOverride ?? CurrentNormalized();
            if (n == _applied) return;
            _applied = n;
            var rt = (RectTransform)transform;
            rt.anchorMin = n.position;
            rt.anchorMax = n.position + n.size;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static Rect CurrentNormalized()
        {
            var w = Mathf.Max(1, Screen.width);
            var h = Mathf.Max(1, Screen.height);
            var s = Screen.safeArea;
            return new Rect(s.x / w, s.y / h, s.width / w, s.height / h);
        }
    }
}
