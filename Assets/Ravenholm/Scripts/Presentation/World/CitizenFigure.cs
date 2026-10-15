using UnityEngine;
using UnityEngine.Rendering;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// A townsperson drawn exactly like the supplier (Kevin, 2026-10-15): the same rubber-hose rig and walk, driven by the
    /// same animator from how the figure moves, with a citizen body set from the character library (shared hands and
    /// boots, the game's outline). For now every NPC is the blond citizen.
    /// </summary>
    public static class CitizenFigure
    {
        /// <summary>Builds a figure under <paramref name="parent"/> at <paramref name="position"/>; returns its root (move the root to walk).</summary>
        public static GameObject Create(Transform parent, string name, Vector2 position, HoseCharacterDefinition body, CharacterAnimationSet animations)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.AddComponent<SortingGroup>();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            var frame = new GameObject("Base");
            frame.transform.SetParent(visual.transform, false);
            var sr = frame.AddComponent<SpriteRenderer>();
            sr.spriteSortPoint = SpriteSortPoint.Pivot;
            var idle = animations != null ? animations.Get("idle_S") ?? animations.Get("idle_front") : null;
            if (idle != null && idle.frames.Length > 0) sr.sprite = idle.frames[0];
            sr.enabled = false;   // the animator's bookkeeping frame; the rig draws the figure
            visual.AddComponent<SupplierAnimator>().EditorSetup(animations, sr);
            visual.AddComponent<HoseRig>().EditorSetup(body);
            go.AddComponent<SpriteOutline>();
            return go;
        }
    }
}
