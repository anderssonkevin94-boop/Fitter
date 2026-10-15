using System.Collections;
using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Applicants who have arrived for the open posting stand at the Store as small characters
    /// (GDD 11). The selected contractor visibly departs; the others drift back to the pool.
    /// </summary>
    public sealed class ApplicantCrowdView : MonoBehaviour
    {
        [SerializeField] private List<Vector2> spots = new List<Vector2>();
        [SerializeField] private Vector2 departurePoint = new Vector2(0f, -8.6f);
        [SerializeField] private List<Sprite> skins = new List<Sprite>();
        [Tooltip("Adventurers are drawn as citizens: the supplier's rig and walk with a citizen body set (2026-10-15).")]
        [SerializeField] private HoseCharacterDefinition citizen;
        [SerializeField] private CharacterAnimationSet animations;

        private GameRoot _root;
        private readonly Dictionary<string, GameObject> _shown = new Dictionary<string, GameObject>();
        private string _key = "";

        public int ShownCount => _shown.Count;

        public void EditorSetup(List<Vector2> spotPositions, List<Sprite> skinSprites)
        {
            spots = spotPositions;
            skins = skinSprites;
        }

        /// <summary>Where adventurers come from and go to (the top of the cliff path, 2026-10-15).</summary>
        public void EditorSetupDeparture(Vector2 point) => departurePoint = point;

        public void EditorSetupCitizen(HoseCharacterDefinition body, CharacterAnimationSet set)
        {
            citizen = body;
            animations = set;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _root.Services.Jobs.Board.Dispatched += OnDispatched;
            _root.Services.Jobs.Reloaded += () => _root.Services.Jobs.Board.Dispatched += OnDispatched;
            _root.Services.Jobs.Completed += OnReturned;
        }

        private readonly Dictionary<string, GameObject> _coming = new Dictionary<string, GameObject>();

        /// <summary>Adventurers walking from the castle to the shop (tests).</summary>
        public int ComingCount => _coming.Count;

        /// <summary>A castle job's adventurer walks up the road from the castle while on the way to the shop.</summary>
        private void ShowComing(JobBoardSystem board, PostingRecord p)
        {
            var keep = new HashSet<string>();
            if (p != null && p.ByCastle)
                foreach (var a in p.Applicants)
                {
                    if (board.HasArrived(a)) continue;
                    var slot = Mathf.Clamp(p.Applicants.IndexOf(a), 0, Mathf.Max(0, spots.Count - 1));
                    var to = spots.Count > 0 ? spots[slot] : Vector2.zero;
                    keep.Add(a.ContractorId);
                    if (!_coming.TryGetValue(a.ContractorId, out var walker))
                        _coming[a.ContractorId] = walker = Spawn(board.Contractor(a.ContractorId), departurePoint, false);
                    var span = Mathf.Max(1f, a.ArriveMs - p.CreatedMs);
                    var k = Mathf.Clamp01((board.State.SimMs - p.CreatedMs) / span);
                    walker.transform.position = Vector2.Lerp(departurePoint, to, k) + Bob(Time.time);
                }
            foreach (var id in new List<string>(_coming.Keys))
            {
                if (keep.Contains(id)) continue;
                Destroy(_coming[id]);
                _coming.Remove(id);
            }
        }

        private void Update()
        {
            var board = _root.Services.Jobs.Board;
            var p = board.State.Posting;
            ShowComing(board, p);
            var arrived = new List<ApplicantRecord>();
            if (p != null) foreach (var a in p.Applicants) if (board.HasArrived(a)) arrived.Add(a);
            var key = (p?.QuestId ?? "") + ":" + string.Join(",", arrived.ConvertAll(a => a.ContractorId));
            if (key == _key) return;
            _key = key;

            // Each applicant keeps the spot of their place in the posting, whatever order they arrive in.
            var keep = new HashSet<string>();
            foreach (var a in arrived)
            {
                var slot = p.Applicants.IndexOf(a);
                if (slot < 0 || slot >= spots.Count) continue;
                keep.Add(a.ContractorId);
                if (!_shown.ContainsKey(a.ContractorId))
                {
                    _shown[a.ContractorId] = Spawn(board.Contractor(a.ContractorId), spots[slot], true);
                    _shown[a.ContractorId].GetComponentInChildren<CharacterIdle>()?.Greet();   // they arrive and say hello
                }
            }
            foreach (var id in new List<string>(_shown.Keys))
            {
                if (keep.Contains(id)) continue;
                Destroy(_shown[id]);
                _shown.Remove(id);
            }
            SandboxSceneController.Current?.RefreshInspectables();
        }

        private GameObject Spawn(ContractorRecord c, Vector2 pos, bool interactive)
        {
            if (citizen != null)
            {
                // Every adventurer is the citizen for now, walking exactly like the supplier.
                var fig = CitizenFigure.Create(transform, c != null ? c.Name : "Applicant", pos, citizen, animations);
                if (!interactive) return fig;
                var box = fig.AddComponent<BoxCollider2D>();
                box.isTrigger = true;
                box.size = new Vector2(0.7f, 1.1f);
                box.offset = new Vector2(0f, 0.5f);
                fig.AddComponent<ApplicantInspectable>().Bind(c?.Id, c?.Name ?? "Applicant");
                return fig;
            }
            var go = new GameObject(c != null ? c.Name : "Applicant");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.AddComponent<SortingGroup>();
            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            var body = visual.gameObject.AddComponent<SpriteRenderer>();
            body.sprite = skins.Count > 0 ? skins[Mathf.Clamp(c?.Skin ?? 0, 0, skins.Count - 1)] : null;
            body.spriteSortPoint = SpriteSortPoint.Pivot;
            var overlayGo = new GameObject("Overlay");
            overlayGo.transform.SetParent(visual, false);
            var overlay = overlayGo.AddComponent<SpriteRenderer>();
            overlay.sortingOrder = 1;
            if (c != null && _root.Services.Catalogue.TryGet<AdventurerClassDefinition>(c.ClassId, out var cls)) overlay.sprite = cls.Overlay;
            if (!interactive) return go;
            visual.gameObject.AddComponent<CharacterIdle>();
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.7f, 1.1f);
            col.offset = new Vector2(0f, 0.5f);
            go.AddComponent<ApplicantInspectable>().Bind(c?.Id, c?.Name ?? "Applicant");
            return go;
        }

        private void OnDispatched(ExpeditionRecord e)
        {
            var c = _root.Services.Jobs.Board.Contractor(e.ContractorId);
            var from = _shown.TryGetValue(e.ContractorId, out var go) ? (Vector2)go.transform.position : spots.Count > 0 ? spots[0] : Vector2.zero;
            StartCoroutine(Depart(Spawn(c, from, false), from));
        }

        private void OnDestroy()
        {
            if (_root != null) _root.Services.Jobs.Completed -= OnReturned;
        }

        /// <summary>Returning contractors walk back up the road to the Store (GDD 20). The dead do not return.</summary>
        private void OnReturned(Domain.Jobs.ResultRecord r)
        {
            if (r.Died || r.Sponsored || !isActiveAndEnabled) return;
            var c = _root.Services.Jobs.Board.Contractor(r.ContractorId);
            var to = spots.Count > 0 ? spots[spots.Count - 1] : Vector2.zero;
            Returning++;
            StartCoroutine(Walk(Spawn(c, departurePoint, false), departurePoint, to, fadeIn: true));
        }

        /// <summary>Returns walked back so far (tests).</summary>
        public int Returning { get; private set; }

        /// <summary>How long a walk takes at the citizen's walking pace (3 s for the old figures).</summary>
        private float WalkSeconds(Vector2 from, Vector2 to) =>
            citizen != null ? Mathf.Max(0.5f, Vector2.Distance(from, to) / Mathf.Max(0.1f, citizen.walkSpeed)) : 3f;

        private Vector2 Bob(float t) => citizen != null || Feel.ReducedMotion ? Vector2.zero : new Vector2(0f, Mathf.Abs(Mathf.Sin(t * 12f)) * 0.05f);

        private IEnumerator Walk(GameObject walker, Vector2 from, Vector2 to, bool fadeIn)
        {
            var seconds = WalkSeconds(from, to);
            for (var t = 0f; t < seconds && walker != null; t += Time.deltaTime)
            {
                var k = t / seconds;
                walker.transform.position = Vector2.Lerp(from, to, k) + Bob(t);
                // Citizens come and go at the castle gate (their hose limbs are meshes, which a sprite fade would leave behind).
                if (citizen == null) foreach (var sr in walker.GetComponentsInChildren<SpriteRenderer>())
                {
                    var col = sr.color;
                    col.a = fadeIn ? (k < 0.2f ? k * 5f : k > 0.85f ? (1f - k) / 0.15f : 1f) : 1f;
                    sr.color = col;
                }
                yield return null;
            }
            if (walker != null) Destroy(walker);
        }

        private IEnumerator Depart(GameObject walker, Vector2 from)
        {
            var seconds = WalkSeconds(from, departurePoint);
            for (var t = 0f; t < seconds && walker != null; t += Time.deltaTime)
            {
                var k = t / seconds;
                walker.transform.position = Vector2.Lerp(from, departurePoint, k) + Bob(t);
                // Citizens come and go at the castle gate (their hose limbs are meshes, which a sprite fade would leave behind).
                if (citizen == null) foreach (var sr in walker.GetComponentsInChildren<SpriteRenderer>())
                {
                    var col = sr.color;
                    col.a = k > 0.8f ? (1f - k) * 5f : 1f;
                    sr.color = col;
                }
                yield return null;
            }
            if (walker != null) Destroy(walker);
        }
    }
}
