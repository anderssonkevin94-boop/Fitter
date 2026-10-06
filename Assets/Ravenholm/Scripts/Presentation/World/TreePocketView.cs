using System.Collections;
using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The reusable Forest pocket (GDD 7): fixed tree spots that show the active clearing's trees
    /// (species from the simulation), stumps once felled, a shake on each chop reward, deeper
    /// areas tinted darker, and trail markers that grow with the area index.
    /// </summary>
    [System.Serializable]
    public struct SpeciesSprite
    {
        public ResourceDefinition resource;
        public Sprite sprite;
    }

    public sealed class TreePocketView : MonoBehaviour
    {
        [Tooltip("Species with their own tree sprite (e.g. ironwood). Checked before the broadleaf list.")]
        [SerializeField] private List<SpeciesSprite> speciesSprites = new List<SpeciesSprite>();
        [SerializeField] private string siteId = "";
        [SerializeField] private List<SpriteRenderer> spots = new List<SpriteRenderer>();
        [SerializeField] private List<GameObject> trailMarkers = new List<GameObject>();
        [SerializeField] private Sprite cedarSprite;
        [SerializeField] private Sprite mapleSprite;
        [SerializeField] private Sprite stumpSprite;
        [Tooltip("Species resource IDs drawn with the maple sprite; everything else uses the cedar sprite.")]
        [SerializeField] private List<ResourceDefinition> broadleafSpecies = new List<ResourceDefinition>();
        [Tooltip("One tint per Forest area: deeper areas are darker and cooler (GDD 7).")]
        [SerializeField] private Color[] areaTints =
        {
            Color.white, new Color(0.88f, 0.92f, 0.86f), new Color(0.74f, 0.82f, 0.76f),
            new Color(0.64f, 0.74f, 0.70f), new Color(0.56f, 0.66f, 0.66f), new Color(0.48f, 0.58f, 0.62f),
        };
        private int _shownDepth = -1;
        private float _fade = 1f;

        private GameRoot _root;
        private readonly Dictionary<int, Coroutine> _shakes = new Dictionary<int, Coroutine>();

        public void EditorSetSpecies(List<SpeciesSprite> species) => speciesSprites = species;

        public void EditorSetup(string site, List<SpriteRenderer> treeSpots, List<GameObject> markers, Sprite cedar, Sprite maple, Sprite stump,
            List<ResourceDefinition> broadleaf)
        {
            siteId = site;
            spots = treeSpots;
            trailMarkers = markers;
            cedarSprite = cedar;
            mapleSprite = maple;
            stumpSprite = stump;
            broadleafSpecies = broadleaf;
        }

        /// <summary>Where a woodcutter stands to chop tree <paramref name="index"/>.</summary>
        public Vector3 WorkSpot(int index) =>
            index >= 0 && index < spots.Count ? spots[index].transform.position + new Vector3(-0.42f, -0.18f, 0f) : transform.position;

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _root.Services.Sites.UnitEarned += OnUnitEarned;
        }

        private void OnDestroy()
        {
            if (_root != null && _root.Services != null) _root.Services.Sites.UnitEarned -= OnUnitEarned;
        }

        private void Update()
        {
            var sim = _root.Services.Sites.Get(siteId);
            if (sim == null) return;
            var s = sim.Record;
            // Pushing deeper loads the next pocket with a short fade (GDD 7); reduced motion switches at once.
            if (s.Depth != _shownDepth)
            {
                if (_shownDepth >= 0 && !Feel.ReducedMotion) _fade = 0f;
                _shownDepth = s.Depth;
            }
            _fade = Mathf.MoveTowards(_fade, 1f, Time.deltaTime * 1.5f);
            var tint = areaTints[Mathf.Clamp(s.Depth - 1, 0, areaTints.Length - 1)];
            tint.a = _fade;
            for (var i = 0; i < spots.Count; i++)
            {
                var sr = spots[i];
                if (i >= s.Trees.Count) { sr.gameObject.SetActive(false); continue; }
                sr.gameObject.SetActive(true);
                var t = s.Trees[i];
                sr.sprite = t.Felled ? stumpSprite : SpriteFor(t.Species);
                sr.color = tint;
            }
            for (var i = 0; i < trailMarkers.Count; i++) trailMarkers[i].SetActive(i < s.Depth);
        }

        public Sprite SpriteFor(string resourceId)
        {
            foreach (var s in speciesSprites) if (s.resource != null && s.resource.Id == resourceId && s.sprite != null) return s.sprite;
            return IsBroadleaf(resourceId) ? mapleSprite : cedarSprite;
        }

        public int ShownDepth => _shownDepth;

        private bool IsBroadleaf(string resourceId)
        {
            foreach (var r in broadleafSpecies) if (r != null && r.Id == resourceId) return true;
            return false;
        }

        private void OnUnitEarned(string site, string worker, int tree, string resource)
        {
            if (site != siteId || tree < 0 || tree >= spots.Count) return;
            if (_shakes.TryGetValue(tree, out var c) && c != null) StopCoroutine(c);
            _shakes[tree] = StartCoroutine(Shake(spots[tree].transform));
        }

        private static IEnumerator Shake(Transform t)
        {
            const float duration = 0.25f;
            for (var e = 0f; e < duration; e += Time.deltaTime)
            {
                t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(e * 60f) * 4f * (1f - e / duration));
                yield return null;
            }
            t.localRotation = Quaternion.identity;
        }
    }
}
