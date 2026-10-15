using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The main button facing the Forest's rock (its hills and ridges): "Dig" the block faced (head height, then legs),
    /// or, standing still on a block, the block underfoot. Its ore (or the Mine's first-layer stone) goes straight into
    /// the carry. Trees and stumps in reach win the button (they are what the Forest is for).
    /// </summary>
    public sealed class ForestRockSpot : Inspectable
    {
        private Collider2D _collider;
        private Vector3Int _block;

        public bool Available { get; private set; }
        public Vector3Int Block => _block;

        private int _gateTier;   // a gate's rock wall: the tool is too weak (0 = it can dig)

        public override string ContextVerb => _gateTier > 0 ? "Needs" : "Dig";
        public override bool OpensOwnPanel => true;
        public override bool Holdable => Available && _gateTier == 0;
        public override bool IsContextAvailable => Available;
        public override float ContextPriority => 0.6f;

        private void Awake() => _collider = GetComponent<Collider2D>();

        private void Update()
        {
            var forest = ForestGridView.Current;
            var root = GameRoot.Instance;
            var player = SandboxSceneController.Current != null ? SandboxSceneController.Current.Player : null;
            var motor = player != null ? player.GetComponent<PlayerMotor>() : null;
            Available = forest != null && root != null && motor != null && !motor.Underground &&
                        forest.RockTarget(player.position, motor.LastMove, root.InputRouter.MoveVector.sqrMagnitude < 0.04f, out _block);
            if (_collider != null) _collider.enabled = Available;
            if (!Available) return;
            transform.position = (Vector2)player.position + new Vector2(0f, 0.1f);   // with the supplier: the nearest when there is rock to dig
            var sites = root.Services.Sites;
            _gateTier = sites.ToolTooWeak(_block.x, _block.y) ? sites.RequiredToolTier(_block.x, _block.y) : 0;
            if (_gateTier > 0)
            {
                EditorSetup(sites.ToolNameOfTier(_gateTier).ToLowerInvariant(), "A rock wall: it needs a better tool (the Forge sells them).", "");
                return;
            }
            var mine = root.Services.Sites.Get("site.mine");
            var ore = mine != null ? mine.SurfaceOreAt(_block.x, _block.y, _block.z) : null;
            EditorSetup(ore != null ? UnitVisuals.DisplayName(ore).ToLowerInvariant() : "rock", "Hold to dig. Ore and stone go straight into your carry.", "");
        }

        public override void OnHold(float deltaSeconds)
        {
            if (!Available) return;
            GameRoot.Instance.Services.Sites.PlayerBreakSurface(_block.x, _block.y, _block.z, deltaSeconds);
        }

        public override void OnInspected()
        {
            if (Available && _gateTier > 0)
            {
                GameRoot.Instance.Hud.ShowToast($"This rock is too hard: you need the {GameRoot.Instance.Services.Sites.ToolNameOfTier(_gateTier)} (at the Forge).");
                return;
            }
            OnHold(0.25f);
        }
    }
}
