using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The settlement trophy (GDD 14): appears in town once the campaign finale has succeeded and stays
    /// for the rest of the save. Derived from saved quest progress, so it needs no state of its own.
    /// </summary>
    public sealed class TrophyView : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Collider2D _collider;
        private Inspectable _inspectable;
        private bool? _shown;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _collider = GetComponent<Collider2D>();
            _inspectable = GetComponent<Inspectable>();
        }

        private void Update()
        {
            var root = GameRoot.Instance;
            var show = root != null && root.Services.Jobs.Board != null && root.Services.Jobs.Board.CampaignComplete;
            if (_shown == show) return;
            _shown = show;
            if (_renderer != null) _renderer.enabled = show;
            if (_collider != null) _collider.enabled = show;
            if (_inspectable != null) _inspectable.enabled = show;
        }

        public bool Shown => _shown == true;
    }
}
