using System.Collections.Generic;
using Ravenholm.Domain.Logistics;
using Ravenholm.Domain.Sites;
using UnityEngine;

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
        [SerializeField] private TreePocketView pocket;
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

        public string SiteId => siteId;
        public string WorkerId => workerId;

        public void EditorSetup(string site, string worker, Transform visualRoot, Transform staging, Transform work, TreePocketView treePocket,
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
            _from = transform.position;
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

            var span = Mathf.Max(1f, w.PhaseEndMs - w.PhaseStartMs);
            var t = Mathf.Clamp01((s.SimMs - w.PhaseStartMs) / span);
            var target = TargetFor(sim, w, out var hidden);
            var timed = w.State == WorkerState.WalkToWork || w.State == WorkerState.Hop || w.State == WorkerState.ReturnToStaging;
            var pos = timed ? Vector3.Lerp(_from, target, t) : target;
            var dx = pos.x - transform.position.x;
            if (timed && Mathf.Abs(dx) > 0.001f) Face(dx);
            transform.position = pos;

            // Mine: fade into the tunnel at the end of the walk, out of it at the start of the return.
            var targetAlpha = hidden ? 0f : 1f;
            if (sim.Config.Kind == SiteKind.Mine)
            {
                if (w.State == WorkerState.WalkToWork) targetAlpha = t > 0.85f ? 0f : 1f;
                if (w.State == WorkerState.ReturnToStaging) targetAlpha = t < 0.1f ? 0f : 1f;
            }
            _alpha = Mathf.MoveTowards(_alpha, targetAlpha, Time.deltaTime * 4f);
            foreach (var r in _renderers) { var c = r.color; c.a = _alpha; r.color = c; }

            // Work swing (Forest is visible; Mine work happens inside the cutaway).
            var swinging = w.State == WorkerState.Working && !hidden;
            visual.localRotation = Quaternion.Euler(0f, 0f, swinging ? Mathf.Sin(Time.time * 9f) * 9f : 0f);

            for (var i = 0; i < _packPieces.Count; i++)
            {
                var on = i < w.Pack.Count && _alpha > 0.5f;
                _packPieces[i].gameObject.SetActive(on);
                if (on) _packPieces[i].sprite = UnitVisuals.StackSprite(CarryUnit.Material(w.Pack[i]));
            }

            UpdateBubble(sim, w);
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
            else if (w.State == WorkerState.AwaitDepthChoice && sim.Record.Status == SiteStatus.AwaitDecision) icon = waitingIcon;
            bubble.gameObject.SetActive(icon != null && _alpha > 0.5f);
            if (icon != null) bubbleIcon.sprite = icon;
        }

        /// <summary>Plain-language reason shown in panels (GDD 5: report why a worker paused).</summary>
        public static string Describe(SiteSimulator sim, WorkerRecord w)
        {
            var mine = sim.Config.Kind == SiteKind.Mine;
            switch (w.State)
            {
                case WorkerState.WalkToWork: return mine ? "Heading down to the face" : "Walking to a tree";
                case WorkerState.Working: return mine ? "Mining the face" : "Chopping";
                case WorkerState.Hop: return "Moving to the next tree";
                case WorkerState.ReturnToStaging: return w.Pack.Count > 0 ? $"Bringing back {w.Pack.Count} unit{(w.Pack.Count == 1 ? "" : "s")}" : "Returning";
                case WorkerState.Depositing: return "Unloading at staging";
                case WorkerState.StagingFull: return $"Paused: staging full ({w.Pack.Count} still in pack). Haul some away.";
                case WorkerState.AwaitDepthChoice:
                    return sim.Record.Status == SiteStatus.AwaitDecision
                        ? (mine ? "Waiting: choose next section" : "Waiting: choose next area")
                        : "Waiting";
                default: return w.State.ToString();
            }
        }
    }
}
