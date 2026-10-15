using Ravenholm.Domain.Sites;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The supplier's axe (solo play, GDD 7): the context button targets the nearest standing tree within reach, any
    /// tree of the Forest's grid (2026-10-12). Holding it chops; the logs go straight into the carry (or wait at the
    /// site when it is full). The older clearings model (no grid) offers its clearing's trees and then the next choice.
    /// </summary>
    public sealed class ForestChopSpot : Inspectable
    {
        private const string SiteId = "site.forest";
        private const float Reach = 1.6f;

        private int _tree = -1;
        private int _area = -1, _slot = -1;
        private bool _hasCell;   // a tree or stump of the grid is targeted ((area, slot) can be negative in a camp's ring)
        private bool _clearing;
        private float _clearHeld;
        private const float ClearSeconds = 0.8f;
        public bool Clearing => _clearing;
        private Vector3Int _cell;
        public int Area => _area;
        public int Slot => _slot;
        public Vector3Int Cell => _cell;
        private string _verb = "";
        private Collider2D _collider;
        private float _fullToastAt = -10f;

        public bool Available { get; private set; }
        public int Tree => _tree;

        public override string ContextVerb => _verb;
        public override bool IsContextAvailable => Available;
        public override bool OpensOwnPanel => true;
        public override bool Holdable => Available && (_tree >= 0 || _hasCell);
        public override float ContextPriority => 1f;

        private void Awake() => _collider = GetComponent<Collider2D>();

        private void Update()
        {
            Available = false;
            _tree = -1;
            _area = _slot = -1;
            _hasCell = false;
            var root = GameRoot.Instance;
            var forest = ForestGridView.Current;
            var player = SandboxSceneController.Current != null ? SandboxSceneController.Current.Player : null;
            var motor = player != null ? player.GetComponent<PlayerMotor>() : null;
            var sim = root != null ? root.Services.Sites.Get(SiteId) : null;
            if (forest != null && sim != null && motor != null && !motor.Underground) Refresh(forest, sim, player.position);
            if (_collider != null) _collider.enabled = Available;
        }

        private void Refresh(ForestGridView forest, SiteSimulator sim, Vector2 me)
        {
            if (sim.GroveMode)
            {
                _clearing = false;
                if (!forest.NearestStanding(me, Reach, out _area, out _slot, out _cell))
                {
                    // No tree in reach: a stump can be pulled out (it won't grow back then).
                    if (!forest.NearestStump(me, Reach, out _area, out _slot, out _cell)) { _area = _slot = -1; return; }
                    _clearing = true;
                    _hasCell = true;
                    _verb = "Clear";
                    transform.position = forest.WorkSpotOf(_cell) + new Vector3(0f, 0.3f, 0f);
                    EditorSetup("stump", "Hold to pull the stump out (a log). Left standing, it grows back into a tree after a while.", "");
                    Available = true;
                    return;
                }
                _hasCell = true;
                _verb = "Chop";
                transform.position = forest.WorkSpotOf(_cell) + new Vector3(0f, 0.3f, 0f);
                EditorSetup(UnitVisuals.DisplayName(sim.SpeciesAt(_area, _slot)).ToLowerInvariant(),
                    "Hold to chop. Any tree you can reach can be cut; its logs go straight into your carry. Further left are new kinds of wood.", "");
                Available = true;
                return;
            }
            var s = sim.Record;
            var best = -1;
            var bestDist = Reach;
            for (var i = 0; i < s.Trees.Count; i++)
            {
                var t = s.Trees[i];
                if (t.Felled || t.ClaimedBy != null) continue;
                var d = Vector2.Distance(me, forest.WorkSpot(i));
                if (d < bestDist) { bestDist = d; best = i; }
            }
            if (best >= 0)
            {
                _tree = best;
                _verb = "Chop";
                transform.position = forest.WorkSpot(best) + new Vector3(0f, 0.3f, 0f);
                EditorSetup(UnitVisuals.DisplayName(s.Trees[best].Species).ToLowerInvariant(), "Hold to chop. Each tree gives its logs straight into your carry.", "");
                Available = true;
                return;
            }
        }

        public override void OnHold(float deltaSeconds)
        {
            var root = GameRoot.Instance;
            if (root == null) return;
            if (_hasCell && _clearing)
            {
                _clearHeld += deltaSeconds * root.Services.Sites.SpeedMultiplier;
                if (_clearHeld >= ClearSeconds && root.Services.Sites.ClearStump(SiteId, _area, _slot)) _clearHeld = 0f;
            }
            else if (_hasCell)
            {
                _clearHeld = 0f;
                if (root.Services.Sites.PlayerChopCell(SiteId, _area, _slot, deltaSeconds) > 0) ForestGridView.Current?.Shake(_cell);
            }
            else if (_tree >= 0) root.Services.Sites.PlayerChop(SiteId, _tree, deltaSeconds);
            else return;
            if (root.Services.Logistics.System.IsFull && Time.unscaledTime > _fullToastAt + 4f)
            {
                _fullToastAt = Time.unscaledTime;
                root.Hud.ShowToast("Carry full: take the logs to the Lumbermill. The rest waits by the stump.");
            }
        }

        public override void OnInspected()
        {
            var root = GameRoot.Instance;
            if (root == null || !Available) return;
            if (Holdable) OnHold(0.25f);   // a tap is one swing
        }
    }
}
