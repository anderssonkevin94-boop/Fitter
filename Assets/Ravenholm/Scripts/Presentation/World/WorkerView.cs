using System.Collections.Generic;
using Ravenholm.Domain.Logistics;
using Ravenholm.Domain.Sites;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Shows one simulated worker in town: walks between staging and the work spot in step with
    /// the simulation's phase timing, enters the Mine mouth (hidden while at the face), chops at a
    /// claimed Forest tree, carries a small visible pack, and shows a pause bubble.
    /// Presentation only: timing and yields come from <see cref="SiteSimulator"/>; animation never
    /// drives the simulation (GDD 20).
    /// </summary>
    public sealed class WorkerView : MonoBehaviour
    {
        [SerializeField] private string siteId = "";
        [SerializeField] private string workerId = "";
        [SerializeField] private Transform visual;
        [SerializeField] private Transform stagingPoint;
        [Tooltip("Mine: the tunnel mouth the miner walks into. Forest: unused (trees come from the pocket).")]
        [SerializeField] private Transform workPoint;
        [SerializeField] private ForestGridView pocket;
        [SerializeField] private SpriteRenderer bubble;
        [SerializeField] private SpriteRenderer bubbleIcon;
        [SerializeField] private Sprite waitingIcon;
        [SerializeField] private Sprite blockedIcon;

        private GameRoot _root;
        private SpriteRenderer[] _renderers;
        private readonly List<SpriteRenderer> _packPieces = new List<SpriteRenderer>();
        private long _seenPhaseStart = -1;
        private WorkerState _seenState;
        private Vector3 _from;
        private float _alpha = 1f;

        /// <summary>The supplier's own figure while they work a routine (null otherwise).</summary>
        public static WorkerView PlayerAvatar { get; private set; }

        public string SiteId => siteId;
        public string WorkerId => workerId;

        public void EditorSetup(string site, string worker, Transform visualRoot, Transform staging, Transform work, ForestGridView treePocket,
            SpriteRenderer bubbleRenderer, SpriteRenderer icon, Sprite waiting, Sprite blocked)
        {
            siteId = site;
            workerId = worker;
            visual = visualRoot;
            stagingPoint = staging;
            workPoint = work;
            pocket = treePocket;
            bubble = bubbleRenderer;
            bubbleIcon = icon;
            waitingIcon = waiting;
            blockedIcon = blocked;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _renderers = visual.GetComponentsInChildren<SpriteRenderer>(true);
            var packRoot = new GameObject("Pack").transform;
            packRoot.SetParent(transform, false);
            packRoot.localPosition = new Vector3(0f, 1.02f, 0f);
            for (var i = 0; i < 4; i++)
            {
                var go = new GameObject($"PackPiece{i}");
                go.transform.SetParent(packRoot, false);
                go.transform.localPosition = new Vector3(0f, i * 0.11f, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 10 + i;
                go.SetActive(false);
                _packPieces.Add(sr);
            }
            _from = transform.position;
        }

        private void OnDestroy()
        {
            if (PlayerAvatar == this) PlayerAvatar = null;
        }

        private bool TryGetWorker(out SiteSimulator sim, out WorkerRecord w)
        {
            sim = _root.Services.Sites.Get(siteId);
            w = sim?.Record.Workers.Find(x => x.Id == workerId);
            return w != null;
        }

        private bool? _present;

        /// <summary>Crew recruits exist in the scene from the start but appear only once hired (GDD 16).</summary>
        private void SetPresent(bool present)
        {
            if (_present == present) return;
            _present = present;
            visual.gameObject.SetActive(present);
            if (!present)
            {
                if (bubble != null) bubble.gameObject.SetActive(false);
                foreach (var p in _packPieces) p.gameObject.SetActive(false);
            }
            var col = GetComponent<Collider2D>();
            if (col != null) col.enabled = present;
            var insp = GetComponent<WorkerInspectable>();
            if (insp != null) insp.enabled = present;
            if (present && stagingPoint != null) transform.position = stagingPoint.position + new Vector3(0.55f, -0.1f, 0f);
            if (present && workerId == SiteSimulator.PlayerId)
            {
                // The routine starts where the supplier stood.
                var player = SandboxSceneController.Current != null ? SandboxSceneController.Current.Player : null;
                if (player != null) transform.position = player.position;
            }
            _from = transform.position;
            if (workerId == SiteSimulator.PlayerId)
            {
                if (present) PlayerAvatar = this;
                else if (PlayerAvatar == this) PlayerAvatar = null;
            }
        }

        private void Update()
        {
            if (!TryGetWorker(out var sim, out var w)) { SetPresent(false); return; }
            SetPresent(true);
            var s = sim.Record;

            if (w.PhaseStartMs != _seenPhaseStart || w.State != _seenState)
            {
                _from = transform.position;
                _seenPhaseStart = w.PhaseStartMs;
                _seenState = w.State;
            }

            if (w.TargetTree >= 0) _lastTree = w.TargetTree;
            var span = Mathf.Max(1f, w.PhaseEndMs - w.PhaseStartMs);
            var t = Mathf.Clamp01((s.SimMs - w.PhaseStartMs) / span);
            var target = TargetFor(sim, w, out var hidden);
            var timed = w.State == WorkerState.WalkToWork || w.State == WorkerState.Hop || w.State == WorkerState.ReturnToStaging;
            var pos = timed ? Vector3.Lerp(_from, target, t) : target;
            var inShaft = false;
            var cutaway = sim.Config.Kind == SiteKind.Mine ? MineDigView.Current : null;
            if (cutaway != null)
            {
                // GDD 6/20: miners stay visible: rim → down the ladder → the block they are clearing (shaft) or along the
                // ledge into their tunnel, and back, in step with the phase timing.
                var index = 0;
                foreach (var o in s.Workers)
                {
                    if (o == w) break;
                    if (o.Face == w.Face) index++;
                }
                cutaway.RouteTo(sim, w, index, _route);
                var staging = stagingPoint.position + new Vector3(0.55f, -0.1f, 0f);
                var spot = _route[_route.Count - 1];
                var tunnel = sim.Tunnel(w.Face);
                var waiting = tunnel != null ? tunnel.Status == SiteStatus.AwaitDecision : s.Status == SiteStatus.AwaitDecision;
                hidden = false;
                switch (w.State)
                {
                    case WorkerState.WalkToWork:
                        pos = Path(t, staging, _route, out inShaft);
                        break;
                    case WorkerState.ReturnToStaging:
                        pos = Path(1f - t, staging, _route, out inShaft);
                        break;
                    case WorkerState.Working:
                    case WorkerState.Hop:
                        pos = spot;
                        inShaft = true;
                        break;
                    case WorkerState.AwaitDepthChoice:
                        pos = waiting ? staging : spot;
                        inShaft = !waiting;
                        break;
                }
            }
            else if (pocket != null && sim.Config.Kind == SiteKind.Forest && (w.TargetTree >= 0 || _lastTree >= 0) &&
                     (w.State == WorkerState.WalkToWork || w.State == WorkerState.ReturnToStaging))
            {
                // GDD 7: woodcutters walk the cut trail: along the forest's bottom edge, then up their area's column.
                var staging = stagingPoint.position + new Vector3(0.55f, -0.1f, 0f);
                pocket.RouteTo(staging, w.TargetTree >= 0 ? w.TargetTree : _lastTree, _route);
                pos = Along(_route, w.State == WorkerState.WalkToWork ? t : 1f - t);
            }
            InShaft = inShaft;
            var dx = pos.x - transform.position.x;
            if (timed && Mathf.Abs(dx) > 0.001f) Face(dx);
            transform.position = pos;

            // Mine without a cutaway (older scenes): fade into the tunnel at the end of the walk, out at the start of the return.
            var targetAlpha = hidden ? 0f : 1f;
            if (sim.Config.Kind == SiteKind.Mine && cutaway == null)
            {
                if (w.State == WorkerState.WalkToWork) targetAlpha = t > 0.85f ? 0f : 1f;
                if (w.State == WorkerState.ReturnToStaging) targetAlpha = t < 0.1f ? 0f : 1f;
            }
            _alpha = Mathf.MoveTowards(_alpha, targetAlpha, Time.deltaTime * 4f);
            foreach (var r in _renderers) { var c = r.color; c.a = _alpha; r.color = c; }

            // Work swing (Forest is visible; Mine work happens inside the cutaway).
            var swinging = w.State == WorkerState.Working && !hidden && !Feel.ReducedMotion;   // the bubble and pack still show state
            visual.localRotation = Quaternion.Euler(0f, 0f, swinging ? Mathf.Sin(Time.time * 9f) * 9f : 0f);

            for (var i = 0; i < _packPieces.Count; i++)
            {
                var on = i < w.Pack.Count && _alpha > 0.5f;
                _packPieces[i].gameObject.SetActive(on);
                if (on) _packPieces[i].sprite = UnitVisuals.StackSprite(CarryUnit.Material(w.Pack[i]));
            }

            UpdateBubble(sim, w);
        }

        /// <summary>True while the miner is down in the pit (tests).</summary>
        public bool InShaft { get; private set; }

        private readonly List<Vector3> _route = new List<Vector3>();
        private int _lastTree = -1;   // the tree a returning woodcutter walks back from

        /// <summary>Point at fraction <paramref name="t"/> of a polyline's length.</summary>
        private static Vector3 Along(List<Vector3> route, float t)
        {
            var total = 0f;
            for (var i = 1; i < route.Count; i++) total += Vector3.Distance(route[i - 1], route[i]);
            var along = Mathf.Clamp01(t) * total;
            for (var i = 1; i < route.Count; i++)
            {
                var d = Vector3.Distance(route[i - 1], route[i]);
                if (along <= d || i == route.Count - 1) return Vector3.Lerp(route[i - 1], route[i], d > 0f ? Mathf.Clamp01(along / d) : 1f);
                along -= d;
            }
            return route[route.Count - 1];
        }

        /// <summary>
        /// Staging → ladder top (first 30 % of the phase) → down the ladder (next 30 %) → along the floor to the spot
        /// (last 40 %, split by distance), by phase fraction (0..1). <paramref name="route"/> starts at the ladder top.
        /// </summary>
        private static Vector3 Path(float t, Vector3 staging, List<Vector3> route, out bool inShaft)
        {
            inShaft = t > 0.3f;
            if (t < 0.3f || route.Count < 2) return Vector3.Lerp(staging, route[0], Mathf.Clamp01(t / 0.3f));
            if (t < 0.6f || route.Count < 3) return Vector3.Lerp(route[0], route[1], Mathf.Clamp01((t - 0.3f) / 0.3f));
            var total = 0f;
            for (var i = 2; i < route.Count; i++) total += Vector3.Distance(route[i - 1], route[i]);
            var along = (t - 0.6f) / 0.4f * total;
            for (var i = 2; i < route.Count; i++)
            {
                var d = Vector3.Distance(route[i - 1], route[i]);
                if (along <= d || i == route.Count - 1) return Vector3.Lerp(route[i - 1], route[i], d > 0f ? Mathf.Clamp01(along / d) : 1f);
                along -= d;
            }
            return route[route.Count - 1];
        }

        private Vector3 TargetFor(SiteSimulator sim, WorkerRecord w, out bool hidden)
        {
            hidden = false;
            var staging = stagingPoint.position + new Vector3(0.55f, -0.1f, 0f);
            switch (w.State)
            {
                case WorkerState.WalkToWork:
                case WorkerState.Hop:
                case WorkerState.Working:
                    if (sim.Config.Kind == SiteKind.Mine)
                    {
                        hidden = w.State == WorkerState.Working;
                        return workPoint.position;
                    }
                    return pocket != null && w.TargetTree >= 0 ? pocket.WorkSpot(w.TargetTree) : staging;
                default:
                    return staging;
            }
        }

        private void Face(float dx)
        {
            var sc = visual.localScale;
            sc.x = Mathf.Abs(sc.x) * (dx < 0 ? -1f : 1f);
            visual.localScale = sc;
        }

        private void UpdateBubble(SiteSimulator sim, WorkerRecord w)
        {
            if (bubble == null) return;
            Sprite icon = null;
            if (w.State == WorkerState.StagingFull) icon = blockedIcon;
            else if (w.State == WorkerState.AwaitDepthChoice && FaceWaiting(sim, w)) icon = waitingIcon;
            bubble.gameObject.SetActive(icon != null && _alpha > 0.5f);
            if (icon != null) bubbleIcon.sprite = icon;
        }

        private static bool FaceWaiting(SiteSimulator sim, WorkerRecord w)
        {
            var t = sim.Tunnel(w.Face);
            return t != null ? t.Status == SiteStatus.AwaitDecision : sim.Record.Status == SiteStatus.AwaitDecision;
        }

        /// <summary>Where a miner digs, in words ("the shaft", "the level 3 left tunnel").</summary>
        public static string FaceName(SiteSimulator sim, int face)
        {
            var t = sim.Tunnel(face);
            return t == null ? "the shaft" : $"the level {t.Level} tunnel";
        }

        /// <summary>Plain-language reason shown in panels (GDD 5: report why a worker paused).</summary>
        public static string Describe(SiteSimulator sim, WorkerRecord w)
        {
            var mine = sim.Config.Kind == SiteKind.Mine;
            switch (w.State)
            {
                case WorkerState.WalkToWork: return mine ? $"Heading down to {FaceName(sim, w.Face)}" : "Walking to a tree";
                case WorkerState.Working: return mine ? $"Mining {FaceName(sim, w.Face)}" : "Chopping";
                case WorkerState.Hop: return "Moving to the next tree";
                case WorkerState.ReturnToStaging: return w.Pack.Count > 0 ? $"Bringing back {w.Pack.Count} unit{(w.Pack.Count == 1 ? "" : "s")}" : "Returning";
                case WorkerState.Depositing: return "Unloading at staging";
                case WorkerState.StagingFull: return $"Paused: staging full ({w.Pack.Count} still in pack). Haul some away.";
                case WorkerState.AwaitDepthChoice:
                    if (mine && sim.Tunnel(w.Face) != null)
                        return FaceWaiting(sim, w) ? $"Waiting: extend {FaceName(sim, w.Face)}" : "Waiting";
                    return sim.Record.Status == SiteStatus.AwaitDecision
                        ? (mine ? "Waiting: choose next section" : "Waiting: choose next area")
                        : "Waiting";
                default: return w.State.ToString();
            }
        }
    }
}
