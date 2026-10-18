using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Ravenholm.EditorTools
{
    /// <summary>
    /// Applies the placeholder kit conventions on import: 128 PPU sprites (or a sprite's own "ppu"), pivots and
    /// 9-slice borders from Art/Placeholder/sprite_meta.json (written by the generator).
    /// Sprite file names carry no logic; all metadata lives in that manifest.
    /// </summary>
    public sealed class PlaceholderSpriteImporter : AssetPostprocessor
    {
        public const string ArtRoot = "Assets/Ravenholm/Art/Placeholder/";
        private const string MetaPath = ArtRoot + "sprite_meta.json";

        private static Dictionary<string, (Vector2 pivot, Vector4 border, bool hasBorder, float ppu)> _meta;
        private static float _ppu = 128f;

        public override uint GetVersion() => 13;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtRoot)) return;
            LoadMeta();

            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = _ppu;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;

            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            var rel = assetPath.Substring(ArtRoot.Length);
            if (_meta.TryGetValue(rel, out var m))
            {
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = m.pivot;
                settings.spriteBorder = m.hasBorder ? m.border : Vector4.zero;
                settings.spriteMeshType = m.hasBorder ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
                if (m.ppu > 0f) settings.spritePixelsPerUnit = m.ppu;   // e.g. the 2x character frames
            }
            else
            {
                Debug.LogWarning($"[PlaceholderSpriteImporter] No pivot metadata for {rel}; using centre.");
                settings.spriteAlignment = (int)SpriteAlignment.Center;
            }
            // Tiles must be full-rect so neighbouring diamonds meet without mesh gaps.
            if (rel.StartsWith("Tiles/")) settings.spriteMeshType = SpriteMeshType.FullRect;
            // Carried pieces keep their whole canvas too (Astra's stack sprites: no trimming, the pivot on the floor line).
            if (rel.StartsWith("Stacks/")) settings.spriteMeshType = SpriteMeshType.FullRect;
            // Astra's modular Blacksmith (2026-10-17): untrimmed canvases, as the pack asks (its points are canvas pixels).
            if (rel.StartsWith("Blacksmith/")) settings.spriteMeshType = SpriteMeshType.FullRect;
            // Astra's modular characters (2026-10-18): heads and torsos on one 512 canvas, untrimmed and not lossily
            // compressed (they meet at zero offset; flat colours stay exact). Skin masks are data: linear, point filtered,
            // uncompressed, and no colour bled into their transparent pixels.
            if (rel.StartsWith("Characters/Modular/"))
            {
                settings.spriteMeshType = SpriteMeshType.FullRect;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                if (rel.Contains("/Masks/"))
                {
                    ti.sRGBTexture = false;
                    ti.alphaIsTransparency = false;
                    settings.filterMode = FilterMode.Point;
                    settings.sRGBTexture = false;
                    settings.alphaIsTransparency = false;
                }
            }
            // Outline-free character parts: the game's outline is drawn inside each sprite's rectangle.
            if (rel.StartsWith("Characters/StoreOwner/") || rel.StartsWith("Characters/Citizens/")) settings.spriteMeshType = SpriteMeshType.FullRect;
            // Ground and blocks fill their shapes edge to edge: full-rect so neighbours meet without mesh gaps.
            if (rel.StartsWith("Painted/Blocks/") || rel.StartsWith("Painted/Ground/")) settings.spriteMeshType = SpriteMeshType.FullRect;
            // Outline-free nature and buildings (v4): the game's outline is drawn inside the sprite's rectangle.
            if (rel.StartsWith("Painted/Nature/") || rel.StartsWith("Nature/") || rel.StartsWith("Buildings/") || rel.StartsWith("Furniture/")) settings.spriteMeshType = SpriteMeshType.FullRect;
            // Astra's ox, eight-direction walk V12 (2026-10-16): full 1280 canvases (pivots on each direction's ground
            // anchor), point filtering as the pack asks; capped at 512 (still above his size on screen) for the phone's memory.
            if (rel.StartsWith("Ox/"))
            {
                ti.maxTextureSize = 512;
                settings.filterMode = FilterMode.Point;   // (the settings are applied last: set it there)
                settings.spriteMeshType = SpriteMeshType.FullRect;
            }
            // Astra's switchback pass (2026-10-16): paintings placed by their anchors, never trimmed. Capped at 2048 and
            // compressed for the phone's memory (distance views under haze); the valley floor repeats.
            if (rel.StartsWith("Pass/"))
            {
                ti.maxTextureSize = 2048;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                if (rel.EndsWith("valley_floor.png")) settings.wrapMode = TextureWrapMode.Repeat;
            }
            // Astra's walk-in wagon (2026-10-16): seven registered layers on one 1536 x 1024 canvas, never trimmed.
            if (rel.StartsWith("Wagon/WalkIn/"))
            {
                ti.maxTextureSize = 2048;
                settings.spriteMeshType = SpriteMeshType.FullRect;
            }
            // Astra's valley view (2026-10-15): 4096-px repeating layers, kept sharp on the phone; full-rect so they tile.
            if (rel.StartsWith("View/"))
            {
                ti.maxTextureSize = 4096;
                settings.spriteMeshType = SpriteMeshType.FullRect;
            }
            ti.SetTextureSettings(settings);
        }

        public static void InvalidateCache() => _meta = null;

        private static void LoadMeta()
        {
            if (_meta != null) return;
            _meta = new Dictionary<string, (Vector2, Vector4, bool, float)>();
            if (!File.Exists(MetaPath))
            {
                Debug.LogError($"[PlaceholderSpriteImporter] {MetaPath} missing. Run Tools/PlaceholderArt/generate_placeholders.py.");
                return;
            }
            var root = JObject.Parse(File.ReadAllText(MetaPath));
            _ppu = root["ppu"]?.Value<float>() ?? 128f;
            foreach (var p in (JObject)root["sprites"])
            {
                var pivot = p.Value["pivot"];
                var v = new Vector2(pivot[0].Value<float>(), pivot[1].Value<float>());
                var b = p.Value["border"];
                var border = b != null ? new Vector4(b[0].Value<float>(), b[1].Value<float>(), b[2].Value<float>(), b[3].Value<float>()) : Vector4.zero;
                var ppu = p.Value["ppu"]?.Value<float>() ?? 0f;
                _meta[p.Key] = (v, border, b != null, ppu);
            }
        }
    }
}
