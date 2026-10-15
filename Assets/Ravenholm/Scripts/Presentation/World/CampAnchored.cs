using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Part of the camp's group (the journey, step 4): the Trader's pad, the trophy, the leftover piles. Laid out at camp 1,
    /// it stands at the same spot relative to whichever camp the company is at; its markers and piles tell the logistics.
    /// </summary>
    [DefaultExecutionOrder(90)]   // after markers and piles have registered (it moves them)
    public sealed class CampAnchored : MonoBehaviour
    {
        private Vector3 _laidOut;
        private GameRoot _root;

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _laidOut = transform.position;
            _root.Services.Camp.Moved += OnMoved;
            Apply();
        }

        private void OnDestroy()
        {
            if (_root != null && _root.Services != null) _root.Services.Camp.Moved -= OnMoved;
        }

        private void OnMoved(int from, int to) => Apply();

        private void Apply()
        {
            if (this == null) return;
            transform.position = _laidOut + (Vector3)_root.Services.Camp.Offset;
            foreach (var r in GetComponentsInChildren<ReceiverView>(true)) r.Reregister(true);
            foreach (var c in GetComponentsInChildren<WorldContainerView>(true)) c.Reregister();
            Physics2D.SyncTransforms();
        }
    }
}
