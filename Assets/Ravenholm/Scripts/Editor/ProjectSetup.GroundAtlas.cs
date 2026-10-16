using System.IO;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Ravenholm.EditorTools
{
    public static partial class ProjectSetup
    {
        public const string GroundAtlasPath = TileDir + "GroundTiles.spriteatlasv2";

        /// <summary>The ground tiles' art on the map, in the order the ground tilemap's tiles use it.</summary>
        public static readonly string[] GroundTileKeys =
            { "grass_a", "grass_b", "grass_c", "path", "path_b", "path_c", "plaza", "dirt", "forest_floor", "forest_floor_b", "bridge" };

        /// <summary>
        /// The ground's tile art packed into one texture (2026-10-16). The ground tilemap draws in Chunk mode (one mesh per
        /// chunk) instead of one sorted item per tile: 26,000 tiles were far over Unity's 4,095 sorting limit (the red text
        /// and the lag on the phone). An isometric tilemap in Chunk mode sorts its tiles' overlapping lips correctly only
        /// when they share one texture.
        /// </summary>
        public static void EnsureGroundAtlas()
        {
            var sprites = new System.Collections.Generic.List<Object>();
            foreach (var key in GroundTileKeys)
            {
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(GroundTileArt(key));
                if (s == null) throw new System.InvalidOperationException("Missing ground tile art: " + GroundTileArt(key));
                sprites.Add(s);
            }
            if (!File.Exists(GroundAtlasPath))
            {
                var atlas = new SpriteAtlasAsset();
                atlas.Add(sprites.ToArray());
                SpriteAtlasAsset.Save(atlas, GroundAtlasPath);
                AssetDatabase.ImportAsset(GroundAtlasPath);
                var importer = (SpriteAtlasImporter)AssetImporter.GetAtPath(GroundAtlasPath);
                importer.packingSettings = new SpriteAtlasPackingSettings { enableRotation = false, enableTightPacking = false, padding = 4, enableAlphaDilation = true };
                importer.textureSettings = new SpriteAtlasTextureSettings { filterMode = FilterMode.Bilinear, generateMipMaps = false, sRGB = true, readable = false };
                importer.includeInBuild = true;
                importer.SaveAndReimport();
                SpriteAtlasUtility.PackAtlases(new[] { AssetDatabase.LoadAssetAtPath<SpriteAtlas>(GroundAtlasPath) }, EditorUserBuildSettings.activeBuildTarget);
            }
        }

        private static string GroundTileArt(string key) => PlaceholderSpriteImporter.ArtRoot + "Tiles/tile_" + key + ".png";
    }
}
