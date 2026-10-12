using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The Dig button (the context button, held): whenever the Mine has a block to dig for the supplier (the wall they
    /// walk into, the block underfoot, or the one they picked), the button offers "Dig" and digs it while held.
    /// </summary>
    public sealed class MineDigSpot : Inspectable
    {
        private Collider2D _collider;

        public override string ContextVerb => "Dig";
        public override bool OpensOwnPanel => true;
        public override bool Holdable => Available;
        public override float ContextPriority => 2f;
        public override bool IsContextAvailable => Available;

        public bool Available { get; private set; }

        private void Awake() => _collider = GetComponent<Collider2D>();

        private void Update()
        {
            var slab = MineSlabView.Current;
            var player = SandboxSceneController.Current != null ? SandboxSceneController.Current.Player : null;
            Available = slab != null && player != null && slab.CanDigNow;
            if (_collider != null) _collider.enabled = Available;
            if (!Available) return;
            // Sits on the supplier, so it is always the nearest target while there is something to dig.
            transform.position = player.position;
            var t = slab.Target.Value;
            var ore = slab.OreAt(t.X, t.U, t.Row);
            EditorSetup(ore != null ? UnitVisuals.DisplayName(ore).ToLowerInvariant() : "rock", "Hold to dig.", "");
        }

        public override void OnHold(float deltaSeconds) => MineSlabView.Current?.Dig(deltaSeconds);

        public override void OnInspected() => MineSlabView.Current?.Dig(0.25f);   // a tap is one swing
    }
}
