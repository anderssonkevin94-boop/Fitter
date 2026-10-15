using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The valley view beyond the north cliff (Astra, 2026-10-15): five horizontally repeating layers, far to near, each
    /// moving by its own fraction of the camera's movement (screen offset = -camera displacement × factor), the castle
    /// fixed on the far ridge (it never repeats), and a tiny traveller walking the valley road toward the castle when an
    /// adventurer leaves. Composition pixels are the town's scale (256 px per unit); Astra's composition places the
    /// cliff's north tip at <see cref="tipPx"/>. Everything draws behind the ground.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after the camera has moved this frame
    public sealed class ParallaxBackdrop : MonoBehaviour
    {
        [Serializable]
        public sealed class Layer
        {
            public Sprite sprite;
            public Sprite fill;
            [Tooltip("Optional: stretched UP from the layer's top (the sky, so the screen above it is never empty).")]
            public Sprite top;
            [Tooltip("Top-left of the layer in Astra's composition (pixels, y down).")]
            public Vector2 originPx;
            [Range(0f, 1f)] public float factor;
            public int order;
        }

        [SerializeField] private List<Layer> layers = new List<Layer>();
        [Tooltip("World position of the cliff's north tip, and where it is in the composition.")]
        [SerializeField] private Vector2 tipWorld;
        [SerializeField] private Vector2 tipPx = new Vector2(2048, 1580);
        [Tooltip("Camera position (relative to the tip) at which the layers sit exactly as composed.")]
        [SerializeField] private Vector2 cameraReference = new Vector2(0f, 1.5f);
        [SerializeField] private float pixelsPerUnit = 256f;
        [Tooltip("How far the cliff lip drops per unit sideways from the tip (both arms run along the grid: 0.5). The view " +
                 "follows the lip, so walking along an arm shows the valley from the lip's height, not the tip's.")]
        [SerializeField] private float lipSlope = 0.5f;
        [SerializeField] private Material material;

        [Header("Castle (on the far layer; never repeats)")]
        [SerializeField] private Sprite castle;
        [SerializeField] private int castleLayer = 1;
        [SerializeField] private Vector2 castleGroundInLayerPx;
        [SerializeField] private float castleScale = 0.62f;

        [Header("Valley traveller (on the mid layer)")]
        [SerializeField] private Sprite walker;
        [SerializeField] private int walkerLayer = 2;
        [SerializeField] private List<Vector2> roadPx = new List<Vector2>();
        [SerializeField] private float walkerSeconds = 30f;

        [Tooltip("How far each layer's bottom row is extended downward (world units).")]
        [SerializeField] private float fillDepth = 40f;

        private Camera _camera;
        private readonly List<(Transform root, List<Transform> copies, Layer layer)> _built = new List<(Transform, List<Transform>, Layer)>();
        private Transform _castle;
        private readonly List<(Transform t, float started)> _travellers = new List<(Transform, float)>();
        private GameRoot _root;

        public static ParallaxBackdrop Current { get; private set; }
        public Transform Castle => _castle;
        public int Travellers => _travellers.Count;

#if UNITY_EDITOR
        public void EditorSetup(List<Layer> l, Vector2 tip, Vector2 tipInComposition, Vector2 cameraRef, Material mat)
        {
            layers = l;
            tipWorld = tip;
            tipPx = tipInComposition;
            cameraReference = cameraRef;
            material = mat;
        }

        public void EditorSetupCastle(Sprite sprite, int layerIndex, Vector2 groundInLayerPx, float scale)
        {
            castle = sprite;
            castleLayer = layerIndex;
            castleGroundInLayerPx = groundInLayerPx;
            castleScale = scale;
        }

        public void EditorSetupWalker(Sprite sprite, int layerIndex, List<Vector2> pathPx)
        {
            walker = sprite;
            walkerLayer = layerIndex;
            roadPx = pathPx;
        }
#endif

        private void Awake() => Current = this;

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_root != null && _root.Services != null) _root.Services.Jobs.Board.Dispatched -= OnDispatched;
        }

        private void Start()
        {
            _camera = SandboxSceneController.Current != null ? SandboxSceneController.Current.WorldCamera : Camera.main;
            foreach (var l in layers)
            {
                var root = new GameObject(l.sprite != null ? l.sprite.name : "Layer").transform;
                root.SetParent(transform, false);
                var copies = new List<Transform>();
                for (var k = -1; k <= 1; k++)
                {
                    var copy = new GameObject("Copy" + k).transform;
                    copy.SetParent(root, false);
                    Renderer(copy, "Art", l.sprite, Vector2.zero, l.order);
                    if (l.fill != null)
                    {
                        var h = l.sprite.rect.height / pixelsPerUnit;
                        var fill = Renderer(copy, "Fill", l.fill, new Vector2(0f, -h), l.order);
                        fill.transform.localScale = new Vector3(1f, fillDepth * pixelsPerUnit / Mathf.Max(1f, l.fill.rect.height), 1f);
                    }
                    if (l.top != null)
                    {
                        var up = Renderer(copy, "Top", l.top, Vector2.zero, l.order);
                        up.transform.localScale = new Vector3(1f, fillDepth * pixelsPerUnit / Mathf.Max(1f, l.top.rect.height), 1f);
                    }
                    copies.Add(copy);
                }
                _built.Add((root, copies, l));
            }
            if (castle != null && castleLayer >= 0 && castleLayer < _built.Count)
            {
                var sr = Renderer(transform, "Castle", castle, Vector2.zero, layers[castleLayer].order + 1);
                sr.transform.localScale = Vector3.one * castleScale;
                _castle = sr.transform;
            }
            _root = GameRoot.Instance;
            if (_root != null)
            {
                _root.Services.Jobs.Board.Dispatched += OnDispatched;
                _root.Services.Jobs.Reloaded += () => _root.Services.Jobs.Board.Dispatched += OnDispatched;
            }
            LateUpdate();
        }

        private SpriteRenderer Renderer(Transform parent, string name, Sprite sprite, Vector2 local, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = "Ground";
            sr.sortingOrder = order;
            if (material != null) sr.sharedMaterial = material;
            return sr;
        }

        /// <summary>Where a layer's top-left sits for the current camera (world units).</summary>
        public Vector2 LayerOrigin(int index)
        {
            var l = layers[index];
            var composed = tipWorld + new Vector2(l.originPx.x - tipPx.x, -(l.originPx.y - tipPx.y)) / pixelsPerUnit;
            var cam = _camera != null ? (Vector2)_camera.transform.position : tipWorld + cameraReference;
            // Along an arm the lip is lower: look from a camera raised by that drop (as if the lip ran level), then lower
            // the whole view by it, so each arm sees the same valley the tip does.
            var drop = Mathf.Abs(cam.x - tipWorld.x) * lipSlope;
            var level = new Vector2(cam.x, cam.y + drop);
            return composed + (level - (tipWorld + cameraReference)) * (1f - l.factor) - new Vector2(0f, drop);
        }

        private void LateUpdate()
        {
            if (_camera == null) return;
            var camX = _camera.transform.position.x;
            for (var i = 0; i < _built.Count; i++)
            {
                var (root, copies, l) = _built[i];
                var origin = LayerOrigin(i);
                var width = l.sprite.rect.width / pixelsPerUnit;
                var k = Mathf.Round((camX - (origin.x + width * 0.5f)) / width);   // keep the middle copy under the camera
                for (var c = 0; c < copies.Count; c++)
                    copies[c].position = new Vector3(origin.x + (k + c - 1) * width, origin.y, 0f);
            }
            if (_castle != null) _castle.position = LayerPoint(castleLayer, castleGroundInLayerPx);
            for (var i = _travellers.Count - 1; i >= 0; i--)
            {
                var (t, started) = _travellers[i];
                var k = (Time.time - started) / walkerSeconds;
                if (t == null || k >= 1f)
                {
                    if (t != null) Destroy(t.gameObject);
                    _travellers.RemoveAt(i);
                    continue;
                }
                t.position = LayerPoint(walkerLayer, RoadPoint(1f - k));   // the polyline runs from the castle: walk it in reverse
            }
        }

        /// <summary>A point given in a layer's own pixels (top-left origin), in the world, on the layer's unrepeated copy.</summary>
        private Vector3 LayerPoint(int layer, Vector2 px) => LayerOrigin(layer) + new Vector2(px.x, -px.y) / pixelsPerUnit;

        private Vector2 RoadPoint(float k)
        {
            if (roadPx.Count == 0) return Vector2.zero;
            var f = Mathf.Clamp01(k) * (roadPx.Count - 1);
            var i = Mathf.Min(roadPx.Count - 2, Mathf.FloorToInt(f));
            return i < 0 ? roadPx[0] : Vector2.Lerp(roadPx[i], roadPx[i + 1], f - i);
        }

        private void OnDispatched(Domain.Jobs.ExpeditionRecord e) => SendTraveller();

        /// <summary>A tiny figure walks the valley road toward the castle (an adventurer setting off).</summary>
        public void SendTraveller()
        {
            if (walker == null || roadPx.Count < 2 || walkerLayer < 0 || walkerLayer >= layers.Count) return;
            var sr = Renderer(transform, "Traveller", walker, Vector2.zero, layers[walkerLayer].order + 1);
            _travellers.Add((sr.transform, Time.time));
            LateUpdate();
        }
    }
}
