using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Outlines an object made of sprites (a character's body, boots, hands and limbs, or a single prop) with the game's
    /// outline (<see cref="OutlineSettings"/>). Behind every outlined piece it keeps a copy that draws the piece's
    /// silhouette grown by the outline's thickness, all of them at the very back of the object's sorting group, so the
    /// pieces cover each other's inner edges and only the object's outer silhouette shows a line.
    /// Pieces are outlined when their sprite is listed in the settings (or always, with <see cref="OutlineAll"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SortingGroup))]
    public sealed class SpriteOutline : MonoBehaviour
    {
        /// <summary>Inside the object's sorting group, behind every piece.</summary>
        public const int OutlineOrder = -30000;

        private static readonly int ColorId = Shader.PropertyToID("_RavenholmOutlineColor");
        private static readonly int PxId = Shader.PropertyToID("_RavenholmOutlinePx");
        private static int _globalsFrame = -1;

        /// <summary>Outline every piece, listed or not (tests, and objects built entirely from outline-free art).</summary>
        public bool OutlineAll { get; set; }

        /// <summary>How many pieces are outlined right now.</summary>
        public int ActiveOutlines { get; private set; }

        private readonly Dictionary<SpriteRenderer, SpriteRenderer> _copies = new Dictionary<SpriteRenderer, SpriteRenderer>();
        private readonly HashSet<SpriteRenderer> _isCopy = new HashSet<SpriteRenderer>();
        private readonly List<SpriteRenderer> _pieces = new List<SpriteRenderer>();
        private OutlineSettings _settings;

        /// <summary>The settings in use (from the sandbox settings unless set, e.g. by a test).</summary>
        public OutlineSettings Settings
        {
            get
            {
                if (_settings == null && GameRoot.Instance != null) _settings = GameRoot.Instance.Config.SandboxSettings.outlines;
                return _settings;
            }
            set => _settings = value;
        }

        /// <summary>Sets the outline's global thickness and colour (once a frame, for every outlined object).</summary>
        public static void ApplyGlobals(OutlineSettings s)
        {
            if (s == null || _globalsFrame == Time.frameCount) return;
            _globalsFrame = Time.frameCount;
            // Global colours are not converted for the linear colour space by Unity: convert, or the brown comes out pale.
            Shader.SetGlobalColor(ColorId, QualitySettings.activeColorSpace == ColorSpace.Linear ? s.color.linear : s.color);
            // The world camera's own pixel height: the phone's screen in play, a capture's size when rendering into one.
            var cam = SandboxSceneController.Current != null ? SandboxSceneController.Current.WorldCamera : Camera.main;
            Shader.SetGlobalFloat(PxId, s.PixelsFor(cam != null ? cam.pixelHeight : Screen.height));
        }

        private void LateUpdate()
        {
            var s = Settings;
            if (s == null || s.material == null) return;
            ApplyGlobals(s);
            GetComponentsInChildren(true, _pieces);
            var active = 0;
            foreach (var sr in _pieces)
            {
                if (_isCopy.Contains(sr)) continue;
                var want = sr.enabled && sr.gameObject.activeInHierarchy && sr.sprite != null && (OutlineAll || s.IsOutlined(sr.sprite));
                _copies.TryGetValue(sr, out var copy);
                if (!want)
                {
                    if (copy != null) copy.enabled = false;
                    continue;
                }
                if (copy == null) copy = MakeCopy(sr, s.material);
                copy.enabled = true;
                copy.sprite = sr.sprite;
                copy.flipX = sr.flipX;
                copy.flipY = sr.flipY;
                copy.sortingLayerID = sr.sortingLayerID;
                copy.sortingOrder = OutlineOrder;
                copy.color = new Color(1f, 1f, 1f, sr.color.a);   // fades with its piece
                active++;
            }
            ActiveOutlines = active;
        }

        private SpriteRenderer MakeCopy(SpriteRenderer piece, Material material)
        {
            var go = new GameObject("Outline");
            go.transform.SetParent(piece.transform, false);   // follows the piece's position, scale, lean and flip
            var copy = go.AddComponent<SpriteRenderer>();
            copy.sharedMaterial = material;
            _copies[piece] = copy;
            _isCopy.Add(copy);
            return copy;
        }
    }
}
