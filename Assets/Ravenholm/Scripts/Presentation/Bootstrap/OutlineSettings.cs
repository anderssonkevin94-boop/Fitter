using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The game-drawn outline (2026-10-14, Kevin): one consistent line around everything, instead of lines painted into
    /// each asset. Its thickness is in screen pixels (scaled with the screen's height), so it is the same on every
    /// asset whatever its resolution or size. It is switched on per sprite: only sprites listed here are outlined (set
    /// from "outline": true in sprite_meta.json by setup), so art that still has painted lines isn't outlined twice.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Presentation/Outline Settings", fileName = "OutlineSettings")]
    public sealed class OutlineSettings : ScriptableObject
    {
        [Tooltip("The outline's colour (the world's dark brown).")]
        public Color color = new Color32(43, 29, 22, 255);
        [Tooltip("Thickness in screen pixels on a screen referenceScreenHeight pixels tall; other screens scale it.")]
        [Min(0f)] public float thicknessPx = 4f;
        [Tooltip("The screen height the thickness is set for (an iPhone 16 Pro, portrait).")]
        [Min(1f)] public float referenceScreenHeight = 2622f;
        [Tooltip("The silhouette material (Ravenholm/SpriteOutlineSilhouette).")]
        public Material material;
        [Tooltip("Sprites drawn without painted outlines: these get the game's outline.")]
        public List<Sprite> outlinedSprites = new List<Sprite>();

        private HashSet<Sprite> _set;

        public bool IsOutlined(Sprite sprite)
        {
            if (sprite == null) return false;
            if (_set == null) _set = new HashSet<Sprite>(outlinedSprites);
            return _set.Contains(sprite);
        }

        /// <summary>The thickness for this screen, in pixels.</summary>
        public float PixelsFor(float screenHeight) => thicknessPx * screenHeight / referenceScreenHeight;

        private void OnValidate() => _set = null;
    }
}
