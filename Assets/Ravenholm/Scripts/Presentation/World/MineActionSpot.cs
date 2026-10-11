using System.Collections.Generic;
using Ravenholm.Domain.Sites;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Context-button targets in the Mine (GDD 6, solo play), placed where the supplier is:
    ///   - the ladder: "Climb down" in town, "Climb up" below;
    ///   - the shaft: hold "Mine shaft" at the next block (from the rim, or standing on the pit floor); once the shaft
    ///     is through its layer, "Dig deeper";
    ///   - the current level's tunnel: "Dig tunnel" at the end of the ledge, hold "Mine tunnel" at its face, and
    ///     "Extend tunnel" once a stretch is cleared.
    /// Every action is a command to the sites service; nothing here edits state.
    /// </summary>
    public sealed class MineActionSpot : Inspectable
    {
        public enum Kind
        {
            Ladder,
            Tunnel,
            Shaft,
        }

        [SerializeField] private Kind kind;

        private static readonly List<MineActionSpot> All = new List<MineActionSpot>();
        private string _verb = "";
        private bool _action;
        private string _detail = "";
        private int _face = -1;
        private Collider2D _collider;
        private float _fullToastAt = -10f;

        public Kind SpotKind => kind;
        public bool Available { get; private set; }

        public void EditorSetup(Kind k) => kind = k;

        public override string ContextVerb => _verb;
        public override bool IsContextAvailable => Available;
        public override bool OpensOwnPanel => _action;
        public override string BuildDetail() => _detail;
        public override bool Holdable => Available && _verb == "Mine";
        public override float ContextPriority => 1f;

        private void Awake() => _collider = GetComponent<Collider2D>();
        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private static PlayerMotor Player =>
            SandboxSceneController.Current != null && SandboxSceneController.Current.Player != null
                ? SandboxSceneController.Current.Player.GetComponent<PlayerMotor>() : null;

        private void Update()
        {
            var mine = MineDigView.Current;
            var root = GameRoot.Instance;
            var sim = root != null ? root.Services.Sites.Get(MineDigView.SiteId) : null;
            var player = Player;
            Available = false;
            if (mine != null && sim != null && player != null && !player.HiddenForRoutine) Refresh(mine, sim, player);
            if (_collider != null) _collider.enabled = Available;
        }

        private void Set(Vector3 at, string verb, string title, bool action, string detail = "", int face = -1)
        {
            transform.position = at;
            _verb = verb;
            _action = action;
            _detail = detail;
            _face = face;
            EditorSetup(title, detail, "");
            Available = true;
        }

        private void Refresh(MineDigView mine, SiteSimulator sim, PlayerMotor player)
        {
            if (player.OnLadder) return;
            var level = player.MineLevel;
            switch (kind)
            {
                case Kind.Ladder:
                    if (mine.DeepestFloor < 1) return;
                    if (level == 0) Set(mine.LadderTop, "Climb down", "ladder", true);
                    else Set(mine.FloorPoint(mine.LadderFootCell, level), "Climb up", "ladder", true);
                    return;

                case Kind.Shaft:
                    // Down is progress: the shaft is worked from its floor (or from the rim before it is dug).
                    if (level != mine.DeepestFloor) return;
                    if (mine.NextShaftBlock(out var block))
                    {
                        // Worked from wherever the supplier stands at the pit's edge: the spot comes to them.
                        var me = (Vector2)player.transform.position;
                        var pit = (Vector2)mine.PitTopCentre + new Vector2(0f, -level * MineDigView.LevelHeight);
                        if (Vector2.Distance(me, pit) > 3.2f) return;
                        var reachAt = Vector2.Distance(me, block) <= 1.4f ? (Vector2)block : me + ((Vector2)block - me).normalized;
                        Set(reachAt, "Mine", "shaft", true, "The shaft sinks one layer per Dig Deeper. Hold to mine.", 0);
                    }
                    else if (sim.ShaftDone && sim.CanGoDeeper)
                        Set(mine.DigFocus + new Vector3(0f, 0.5f, 0f), "Dig deeper", "shaft", true);
                    return;
            }

            if (level < 2) return;
            // Left is staying in a layer: this level's tunnel, from the end of the ledge out to its face.
            var path = mine.TunnelPath;
            var index = sim.TunnelIndex(level);
            if (index < 0)
            {
                if (sim.CanStartTunnel(level) && path.Count > 0)
                    Set(mine.FloorPoint(mine.LedgeCells[0], level), "Dig", "tunnel", true);
                return;
            }
            var t = sim.Record.Tunnels[index];
            var dug = Mathf.Min(mine.TunnelDug(level), path.Count);
            var cells = sim.TunnelCellsDug(t);
            var detail = $"Level {level} tunnel, in the depth {t.Depth} layer: {cells} block{(cells == 1 ? "" : "s")} dug so far. " +
                         $"Each stretch is one section (effort {sim.TunnelDurability(t)} now, rising each time you continue).";
            if (t.Status == SiteStatus.Working && dug < path.Count)
            {
                Set(mine.FloorPoint(path[dug], level), "Mine", "tunnel", true, detail, index + 1);
                return;
            }
            var at = dug > 0 ? mine.FloorPoint(path[dug - 1], level) : mine.FloorPoint(mine.LedgeCells[0], level);
            if (t.Status == SiteStatus.AwaitDecision && cells + sim.Config.TunnelStretchCells <= path.Count)
                Set(at, "Extend", "tunnel", true, detail);
            else
                Set(at, "Inspect", "tunnel", false, t.Status == SiteStatus.AwaitDecision ? detail + " It has reached the end of the charted frontier." : detail);
        }

        /// <summary>Holding the button on a face mines it: units go straight into the carry.</summary>
        public override void OnHold(float deltaSeconds)
        {
            var root = GameRoot.Instance;
            if (root == null || !Holdable || _face < 0) return;
            root.Services.Sites.PlayerMine(MineDigView.SiteId, _face, deltaSeconds);
            if (root.Services.Logistics.System.IsFull && Time.unscaledTime > _fullToastAt + 4f)
            {
                _fullToastAt = Time.unscaledTime;
                root.Hud.ShowToast("Carry full: take the ore to the Smelter. The rest waits at the face.");
            }
        }

        public override void OnInspected()
        {
            var root = GameRoot.Instance;
            var mine = MineDigView.Current;
            var player = Player;
            if (root == null || mine == null || player == null || !Available) return;
            var sites = root.Services.Sites;
            var sim = sites.Get(MineDigView.SiteId);
            if (kind == Kind.Ladder)
            {
                player.ClimbTo(player.MineLevel == 0 ? mine.DeepestFloor : 0);
                return;
            }
            if (Holdable)
            {
                OnHold(0.25f);   // a tap is one swing
                return;
            }
            if (!_action) return;
            if (kind == Kind.Shaft)
            {
                if (sites.ChooseDeeper(MineDigView.SiteId)) root.Hud.ShowToast($"Digging deeper: depth {sim.Record.Depth}");
                return;
            }
            var level = player.MineLevel;
            var index = sim.TunnelIndex(level);
            if (index >= 0)
            {
                if (sites.ExtendTunnel(MineDigView.SiteId, index + 1)) root.Hud.ShowToast("Tunnel extended");
                return;
            }
            if (sites.StartTunnel(MineDigView.SiteId, level) == 0) return;
            FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
            root.Hud.ShowToast($"Level {level} tunnel started: hold Mine at its face");
        }

        /// <summary>Tests: the spot of a kind, if the scene has it.</summary>
        public static MineActionSpot Find(Kind k) => All.Find(s => s.kind == k);
    }
}
