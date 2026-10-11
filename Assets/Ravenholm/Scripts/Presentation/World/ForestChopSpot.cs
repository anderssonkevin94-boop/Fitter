using Ravenholm.Domain.Sites;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The supplier's axe (solo play, GDD 7): the context button targets the nearest tree of the current clearing within
    /// reach. Holding it chops; each felled share goes straight into the carry (or waits at the stump when full). Once
    /// the clearing is cut, the button offers the next choice (continue up this area, or push deeper left).
    /// </summary>
    public sealed class ForestChopSpot : Inspectable
    {
        private const string SiteId = "site.forest";
        private const float Reach = 1.6f;

        private int _tree = -1;
        private string _verb = "";
        private Collider2D _collider;
        private float _fullToastAt = -10f;

        public bool Available { get; private set; }
        public int Tree => _tree;

        public override string ContextVerb => _verb;
        public override bool IsContextAvailable => Available;
        public override bool OpensOwnPanel => true;
        public override bool Holdable => Available && _tree >= 0;
        public override float ContextPriority => 1f;

        private void Awake() => _collider = GetComponent<Collider2D>();

        private void Update()
        {
            Available = false;
            _tree = -1;
            var root = GameRoot.Instance;
            var forest = ForestGridView.Current;
            var player = SandboxSceneController.Current != null ? SandboxSceneController.Current.Player : null;
            var motor = player != null ? player.GetComponent<PlayerMotor>() : null;
            var sim = root != null ? root.Services.Sites.Get(SiteId) : null;
            if (forest != null && sim != null && motor != null && !motor.HiddenForRoutine && !motor.Underground) Refresh(forest, sim, player.position);
            if (_collider != null) _collider.enabled = Available;
        }

        private void Refresh(ForestGridView forest, SiteSimulator sim, Vector2 me)
        {
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
            // The clearing is cut: the next choice is up the area or further left.
            if (s.Status == SiteStatus.AwaitDecision)
            {
                foreach (var t in s.Trees)
                    if (Vector2.Distance(me, forest.WorkSpot(t.Index)) < Reach * 2f)
                    {
                        _verb = "Choose";
                        transform.position = me + new Vector2(0f, 0.4f);
                        EditorSetup("next trees", "This clearing is cut. Continue up this area, or push deeper left.", "");
                        Available = true;
                        return;
                    }
            }
        }

        public override void OnHold(float deltaSeconds)
        {
            var root = GameRoot.Instance;
            if (root == null || _tree < 0) return;
            root.Services.Sites.PlayerChop(SiteId, _tree, deltaSeconds);
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
            if (Holdable) { OnHold(0.25f); return; }   // a tap is one swing
            root.Hud.ShowSite(SiteId);
        }
    }
}
