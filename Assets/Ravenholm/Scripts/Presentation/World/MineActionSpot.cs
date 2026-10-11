using System.Collections.Generic;
using Ravenholm.Domain.Sites;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Context-button targets in the Mine (GDD 6), placed where the player is: the ladder ("Climb down" in town,
    /// "Climb up" below), and at each end of the current level's ledge either "Dig tunnel" (start one there and send a
    /// miner) or, at a tunnel's face, "Extend tunnel" once its stretch is cleared. Every action is a command to the
    /// sites service; nothing here edits state.
    /// </summary>
    public sealed class MineActionSpot : Inspectable
    {
        public enum Kind
        {
            Ladder,
            Tunnel,
        }

        [SerializeField] private Kind kind;

        private static readonly List<MineActionSpot> All = new List<MineActionSpot>();
        private string _verb = "";
        private bool _action;
        private string _detail = "";
        private Collider2D _collider;

        public Kind SpotKind => kind;
        public bool Available { get; private set; }

        public void EditorSetup(Kind k) => kind = k;

        public override string ContextVerb => _verb;
        public override bool IsContextAvailable => Available;
        public override bool OpensOwnPanel => _action;
        public override string BuildDetail() => _detail;

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
            if (mine != null && sim != null && player != null) Refresh(mine, sim, player);
            if (_collider != null) _collider.enabled = Available;
        }

        private void Set(Vector3 at, string verb, string title, bool action, string detail = "")
        {
            transform.position = at;
            _verb = verb;
            _action = action;
            _detail = detail;
            EditorSetup(title, detail, "");
            Available = true;
        }

        private void Refresh(MineDigView mine, SiteSimulator sim, PlayerMotor player)
        {
            if (player.OnLadder || mine.DeepestFloor < 1) return;
            var level = player.MineLevel;
            if (kind == Kind.Ladder)
            {
                if (level == 0) Set(mine.LadderTop, "Climb down", "ladder", true);
                else Set(mine.FloorPoint(mine.LadderFootCell, level), "Climb up", "ladder", true);
                return;
            }
            if (level < 2) return;
            // At this level's end of the ledge: start its tunnel ("Continue Here" at this level), or at the tunnel's
            // face extend it once the stretch is cleared.
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
            var at = dug > 0 ? mine.FloorPoint(path[dug - 1], level) : mine.FloorPoint(mine.LedgeCells[0], level);
            var cells = sim.TunnelCellsDug(t);
            var detail = $"Level {level} tunnel, in the depth {t.Depth} layer: {cells} block{(cells == 1 ? "" : "s")} dug so far. " +
                         $"Each stretch is one section (effort {sim.TunnelDurability(t)} now, rising each time you continue).";
            if (t.Status == SiteStatus.AwaitDecision && cells + sim.Config.TunnelStretchCells <= path.Count)
                Set(at, "Extend", "tunnel", true, detail);
            else
                Set(at, "Inspect", "tunnel", false, t.Status == SiteStatus.AwaitDecision ? detail + " It has reached the end of the charted frontier." : detail);
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
            if (!_action) return;
            var level = player.MineLevel;
            var index = sim.TunnelIndex(level);
            if (index >= 0)
            {
                if (sites.ExtendTunnel(MineDigView.SiteId, index + 1)) root.Hud.ShowToast("Tunnel extended");
                return;
            }
            var miner = PickMiner(sim, out var leavesShaft);
            var face = sites.StartTunnel(MineDigView.SiteId, level, miner?.Id);
            if (face == 0) return;
            FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
            if (miner == null) root.Hud.ShowToast($"Tunnel marked at level {level}. Send a miner from the Mine panel.");
            else root.Hud.ShowToast(leavesShaft && !sim.ShaftDone
                ? $"{miner.Name} digs the level {level} tunnel. The shaft waits until you send a miner back (Mine panel)."
                : $"{miner.Name} digs the level {level} tunnel");
        }

        /// <summary>
        /// Who digs a new tunnel: a miner whose tunnel waits for you, else one of several on the shaft (the last hired),
        /// else the only shaft miner (the shaft then waits; the panel can send them back).
        /// </summary>
        public static WorkerRecord PickMiner(SiteSimulator sim, out bool leavesShaft)
        {
            leavesShaft = false;
            var workers = sim.Record.Workers;
            foreach (var w in workers)
            {
                var t = sim.Tunnel(w.Face);
                if (t != null && t.Status == SiteStatus.AwaitDecision) return w;
            }
            var onShaft = workers.FindAll(w => sim.Tunnel(w.Face) == null);
            if (onShaft.Count > 1) return onShaft[onShaft.Count - 1];
            if (onShaft.Count == 1) { leavesShaft = true; return onShaft[0]; }
            return null;
        }

        /// <summary>Tests: the spot of a kind, if the scene has it.</summary>
        public static MineActionSpot Find(Kind k) => All.Find(s => s.kind == k);
    }
}
