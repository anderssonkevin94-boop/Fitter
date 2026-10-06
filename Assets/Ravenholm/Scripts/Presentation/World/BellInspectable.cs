using System.Collections;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>PHASE 0 FIXTURE: the Store counter bell increments a value that must survive a restart.</summary>
    public sealed class BellInspectable : Inspectable
    {
        [SerializeField] private Transform visual;
        private Coroutine _wobble;

        public void EditorSetupVisual(Transform v) => visual = v;

        public override void OnInspected()
        {
            var root = GameRoot.Instance;
            if (root == null) return;
            root.Services.Sandbox.RingBell();
            if (visual != null)
            {
                if (_wobble != null) StopCoroutine(_wobble);
                _wobble = StartCoroutine(Wobble());
            }
        }

        public override string BuildDetail()
        {
            var root = GameRoot.Instance;
            var count = root != null ? root.Services.Sandbox.State.BellRingCount : 0;
            return $"Rung <b>{count}</b> time{(count == 1 ? "" : "s")} across all sessions.\n" +
                   "Each ring is saved immediately. Quit the app, relaunch, and the count should be unchanged.\n\n" +
                   "<b>Planned:</b> sandbox persistence sample only; not a game feature.";
        }

        private IEnumerator Wobble()
        {
            const float duration = 0.45f;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                var k = 1f - t / duration;
                visual.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 40f) * 18f * k);
                yield return null;
            }
            visual.localRotation = Quaternion.identity;
            _wobble = null;
        }
    }
}
