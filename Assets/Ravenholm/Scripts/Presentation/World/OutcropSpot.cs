using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The main button beside a surface outcrop: "Mine" its top block while held; the ore goes straight into the carry.
    /// </summary>
    public sealed class OutcropSpot : Inspectable
    {
        private Collider2D _collider;
        private Vector3Int _target;

        public bool Available { get; private set; }
        public Vector3Int Target => _target;

        public override string ContextVerb => "Mine";
        public override bool OpensOwnPanel => true;
        public override bool Holdable => Available;
        public override bool IsContextAvailable => Available;
        public override float ContextPriority => 1.5f;

        private void Awake() => _collider = GetComponent<Collider2D>();

        private void Update()
        {
            var view = SurfaceOutcropView.Current;
            var player = SandboxSceneController.Current != null ? SandboxSceneController.Current.Player : null;
            var motor = player != null ? player.GetComponent<PlayerMotor>() : null;
            Available = view != null && motor != null && !motor.Underground && view.Nearest(player.position, out _target);
            if (_collider != null) _collider.enabled = Available;
            if (!Available) return;
            transform.position = view.BlockTop(_target.x, _target.y, 0);
            var sim = GameRoot.Instance.Services.Sites.Get("site.mine");
            var ore = sim != null ? sim.SurfaceOreAt(_target.x, _target.y, _target.z) : null;
            EditorSetup(ore != null ? UnitVisuals.DisplayName(ore).ToLowerInvariant() : "rock", "Hold to mine the top block. Its ore goes straight into your carry.", "");
        }

        public override void OnHold(float deltaSeconds)
        {
            if (!Available) return;
            GameRoot.Instance.Services.Sites.PlayerBreakSurface(_target.x, _target.y, _target.z, deltaSeconds);
        }

        public override void OnInspected() => OnHold(0.25f);   // a tap is one swing
    }
}
