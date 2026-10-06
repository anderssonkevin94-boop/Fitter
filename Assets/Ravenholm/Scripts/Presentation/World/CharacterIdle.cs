using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Small procedural life for standing characters (GDD 20 adventurer idle/greet): a slow breath and an
    /// optional greeting hop when they arrive. Visual only; reduced motion holds them still.
    /// </summary>
    public sealed class CharacterIdle : MonoBehaviour
    {
        private Vector3 _base;
        private float _phase;
        private float _greet = -1f;

        public bool Greeting => _greet >= 0f;

        public void Greet() => _greet = 0f;

        private void Awake()
        {
            _base = transform.localPosition;
            _phase = Random.value * 6.28f;
        }

        private void Update()
        {
            if (Feel.ReducedMotion)
            {
                transform.localPosition = _base;
                transform.localScale = new Vector3(Mathf.Sign(transform.localScale.x), 1f, 1f);
                _greet = -1f;
                return;
            }
            _phase += Time.deltaTime * 2.2f;
            var hop = 0f;
            if (_greet >= 0f)
            {
                _greet += Time.deltaTime;
                hop = _greet < 0.5f ? Mathf.Sin(_greet / 0.5f * Mathf.PI) * 0.18f : 0f;
                if (_greet > 0.6f) _greet = -1f;
            }
            transform.localPosition = _base + new Vector3(0f, hop, 0f);
            var breath = 1f + Mathf.Sin(_phase) * 0.015f;
            transform.localScale = new Vector3(Mathf.Sign(transform.localScale.x) * (2f - breath), breath, 1f);
        }
    }
}
