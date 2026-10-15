using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The block the supplier is about to dig is highlighted (2026-10-15, Kevin): a bright outline with a faint tint
    /// (Props/fx_block_highlight, drawn on the blocks' canvas), pulsing gently, brighter when tapped/selected. Used by the
    /// quarry, the Forest's rock and the town's outcrops.
    /// </summary>
    public static class BlockHighlight
    {
        public static Color Tint(bool selected)
        {
            var pulse = Feel.ReducedMotion ? 0f : 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
            return new Color(1f, 1f, 1f, selected ? 0.95f : 0.65f + 0.25f * pulse);
        }

        public static SpriteRenderer Make(Transform parent, Sprite sprite, Material material)
        {
            var go = new GameObject("BlockHighlight");
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            if (material != null) sr.sharedMaterial = material;
            sr.enabled = false;
            return sr;
        }
    }
}
