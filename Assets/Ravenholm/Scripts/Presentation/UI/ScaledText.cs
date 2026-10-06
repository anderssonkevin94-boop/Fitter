using TMPro;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Applies the player's text-size setting to one TMP text. The authored size is captured on the first
    /// frame (after the creating code has set it), then rescaled whenever the setting changes.
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class ScaledText : MonoBehaviour
    {
        private TextMeshProUGUI _text;
        private float _base, _baseMin, _baseMax;
        private bool _captured;
        private float _applied = -1f;

        private void LateUpdate()
        {
            if (!_captured)
            {
                _text = GetComponent<TextMeshProUGUI>();
                _base = _text.fontSize;
                _baseMin = _text.fontSizeMin;
                _baseMax = _text.fontSizeMax;
                _captured = true;
            }
            var scale = Feel.TextScale;
            if (Mathf.Approximately(scale, _applied)) return;
            _applied = scale;
            _text.fontSize = _base * scale;
            _text.fontSizeMax = _baseMax * scale;
            _text.fontSizeMin = Mathf.Min(_baseMin * scale, _text.fontSizeMax);
        }
    }
}
