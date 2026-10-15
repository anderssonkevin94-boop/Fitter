using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The horizon ahead (Astra's valley view, re-used for the journey, Kevin 2026-10-15): the view's layers sit in a band
    /// across the top of the screen, behind the land, so they show wherever the ridge ends: past its edges and its far
    /// end. Below the band the camera's background is the valley's colour. The layers repeat sideways and slide by their
    /// own fraction of the camera's sideways movement (far ones barely move); up and down they stay with the screen. The
    /// castle stands on the far ridge (it never repeats), and a tiny traveller walks the valley road toward it when an
    /// adventurer leaves. Composition pixels are 256 per unit before <see cref="scale"/>.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after the camera has moved this frame
    public sealed class ParallaxBackdrop : MonoBehaviour
    {
        [Serializable]
        public sealed class Layer
        {
            public Sprite sprite;
            [Tooltip("Optional: stretched UP from the layer's top (the sky, so the screen above it is never empty).")]
            public Sprite top;
            [Tooltip("Top-left of the layer in Astra's composition (pixels, y down).")]
            public Vector2 originPx;
            [Range(0f, 1f)] public float factor;
            public int order;
        }

        [SerializeField] private List<Layer> layers = new List<Layer>();
        [SerializeField] private float pixelsPerUnit = 256f;
        [Tooltip("The whole view's size (1 = as painted). Smaller keeps the band at the top of the screen thin.")]
        [SerializeField] private float scale = 0.45f;
        [Tooltip("Where the composition's top sits, in units below the top of the screen.")]
        [SerializeField] private float topBelowScreen = 0f;
        [SerializeField] private Material material;
        [Tooltip("The composition's pixel row where the view ends (the near layer's bottom); it fades into the valley colour above it.")]
        [SerializeField] private float bottomPx = 2000f;
        [SerializeField] private float fadeHeight = 1.4f;
        [SerializeField] private Color valleyColour = new Color32(112, 155, 92, 255);

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

        [Tooltip("How far the sky's top row is extended upward (world units).")]
        [SerializeField] private float fillDepth = 40f;

        public const string SortingLayer = "Ground";
        public const int BaseOrder = -30000;   // behind the land, its edge faces and everything on it

        private Camera _camera;
        private readonly List<(Transform root, List<Transform> copies, Layer layer)> _built = new List<(Transform, List<Transform>, Layer)>();
        private Transform _castle;
        private SpriteRenderer _fade;
        private readonly List<(Transform t, float started)> _travellers = new List<(Transform, float)>();
        private GameRoot _root;

        public static ParallaxBackdrop Current { get; private set; }
        public Transform Castle => _castle;

        /// <summary>The world height of the view's top edge (it stays with the screen).</summary>
        public float TopY => ScreenTop() - topBelowScreen;
        public int Travellers => _travellers.Count;

#if UNITY_EDITOR
        public void EditorSetup(List<Layer> l, float viewScale, Color valley, Material mat)
        {
            layers = l;
            scale = viewScale;
            valleyColour = valley;
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
                    copy.localScale = Vector3.one * scale;
                    Renderer(copy, "Art", l.sprite, Vector2.zero, l.order);
                    if (l.top != null)
                    {
                        var up = Renderer(copy, "Top", l.top, Vector2.zero, l.order);
                        up.transform.localScale = new Vector3(1f, fillDepth * pixelsPerUnit / Mathf.Max(1f, l.top.rect.height), 1f);
                    }
                    copies.Add(copy);
                }
                _built.Add((root, copies, l));
            }
            // The view's bottom edge melts into the valley colour (the camera's background, past the ridge's edges).
            _fade = Renderer(transform, "Fade", FadeSprite(), Vector2.zero, BaseOrder + 40);
            _fade.color = valleyColour;
            if (castle != null && castleLayer >= 0 && castleLayer < _built.Count)
            {
                var sr = Renderer(transform, "Castle", castle, Vector2.zero, layers[castleLayer].order + 1);
                sr.transform.localScale = Vector3.one * castleScale * scale;
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
            sr.sortingLayerName = SortingLayer;
            sr.sortingOrder = order;
            if (material != null) sr.sharedMaterial = material;
            return sr;
        }

        private float ScreenTop() => _camera != null ? _camera.transform.position.y + _camera.orthographicSize : 0f;

        /// <summary>Where a layer's top-left sits for the current camera (world units).</summary>
        public Vector2 LayerOrigin(int index)
        {
            var l = layers[index];
            var camX = _camera != null ? _camera.transform.position.x : 0f;
            // Centred on the castle: it stands straight ahead of the camp (the road winds either side of it).
            var centre = -castleGroundInLayerPx.x * scale / pixelsPerUnit;
            return new Vector2(centre + l.originPx.x * scale / pixelsPerUnit + camX * (1f - l.factor),
                               ScreenTop() - topBelowScreen - l.originPx.y * scale / pixelsPerUnit);
        }

        /// <summary>A vertical ramp, opaque at the bottom (its pivot): 1 unit wide, 16 tall.</summary>
        private static Sprite FadeSprite()
        {
            const int h = 64;
            var tex = new Texture2D(4, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[4 * h];
            for (var y = 0; y < h; y++)
            {
                var a = (byte)Mathf.RoundToInt(255f * Mathf.SmoothStep(1f, 0f, y / (h - 1f)));
                for (var x = 0; x < 4; x++) px[y * 4 + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 4, h), new Vector2(0.5f, 0f), 4f);
        }

        private void LateUpdate()
        {
            if (_camera == null) return;
            var camX = _camera.transform.position.x;
            for (var i = 0; i < _built.Count; i++)
            {
                var (root, copies, l) = _built[i];
                var origin = LayerOrigin(i);
                var width = l.sprite.rect.width * scale / pixelsPerUnit;
                var k = Mathf.Round((camX - (origin.x + width * 0.5f)) / width);   // keep the middle copy under the camera
                for (var c = 0; c < copies.Count; c++)
                    copies[c].position = new Vector3(origin.x + (k + c - 1) * width, origin.y, 0f);
            }
            if (_fade != null)
            {
                _fade.transform.position = new Vector3(camX, TopY - bottomPx * scale / pixelsPerUnit + 0.02f, 0f);
                _fade.transform.localScale = new Vector3(_camera.orthographicSize * _camera.aspect * 2f + 4f, fadeHeight / 16f, 1f);
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
        private Vector3 LayerPoint(int layer, Vector2 px) => LayerOrigin(layer) + new Vector2(px.x, -px.y) * scale / pixelsPerUnit;

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
