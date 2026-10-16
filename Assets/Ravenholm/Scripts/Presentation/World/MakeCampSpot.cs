using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The signpost in the next camp's clearing (the journey, step 4): standing in the clearing, the main button offers
    /// "Make camp" and opens the moving-on panel. It stands in the clearing after the one the company is at.
    /// </summary>
    public sealed class MakeCampSpot : Inspectable
    {
        private GameRoot _root;
        private SpriteRenderer _sign;
        private Collider2D _collider;

        public bool Available { get; private set; }

        public override string ContextVerb => "Make camp";
        public override bool OpensOwnPanel => true;
        public override bool IsContextAvailable => Available;
        public override float ContextPriority => 0.8f;

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _sign = GetComponent<SpriteRenderer>();
            _collider = GetComponent<Collider2D>();
            _root.Services.Camp.Moved += (_, __) => Place();
            Place();
        }

        private void Place()
        {
            if (this == null) return;
            var camp = _root.Services.Camp;
            var has = camp.HasNext && !_root.Services.Catalogue.CurrentCamp.Plateau;   // levels: the gate leads on (step 2)
            if (_sign != null) _sign.enabled = has;
            if (!has) return;
            transform.position = camp.CentreOf(camp.Index + 1);
            EditorSetup($"Make camp at {camp.NextName}", "Break camp and move here: the stations pack into their kits, the shop folds onto the wagon, and the ox brings it up the road.", "");
        }

        private void Update()
        {
            var player = SandboxSceneController.Current != null ? SandboxSceneController.Current.Player : null;
            var on = _root.Services.Camp.HasNext && !_root.Services.Catalogue.CurrentCamp.Plateau;
            Available = on && player != null && _root.Services.Camp.AtNext(player.position);
            if (_collider != null) _collider.enabled = on;
        }

        public override void OnInspected()
        {
            if (_root == null || !_root.Services.Camp.HasNext) return;
            _root.Hud.ShowMoveOn();
        }
    }
}
