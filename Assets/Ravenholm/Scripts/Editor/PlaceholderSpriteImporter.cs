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

        public override uint GetVersion() => 6;

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
