using NUnit.Framework;
using Ravenholm.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Astra's ground tiles and blocks (2026-10-14): 256 px per unit, pivot at the top face's centre, full-rect, not
    /// outlined by the game - the same world size and anchor as the art they replaced.
    /// </summary>
    public sealed class WorldTileTests
    {
        private static readonly string[] Blocks = { "basalt", "grass", "limestone", "mossy", "ochre", "ore_coal", "ore_copper", "ore_gold", "ore_iron", "ore_moonsilver", "purple", "red_clay", "sandstone", "sandy", "slate", "topsoil", "topsoil_pebbles" };
        private static readonly string[] Ground = { "cobble", "cobble_grass", "dirt_dug", "forest_grass", "grass_flowers", "grass_plain", "grass_tufts", "path_footprints", "path_pebbles", "path_plain", "planks", "soil_mossy" };
        private static readonly string[] Town = { "grass_a", "grass_b", "grass_c", "forest_floor", "forest_floor_b", "path", "path_b", "path_c", "dirt", "plaza" };

        private static void Check(string rel, Vector2 size)
        {
            var s = ProjectSetup.LoadSprite(rel);
            Assert.NotNull(s, rel);
            Assert.AreEqual(256f, s.pixelsPerUnit, 0.01f, rel + ": 256 px per unit");
            Assert.AreEqual(size, s.rect.size, rel + ": the full canvas");
            Assert.AreEqual(1f, s.bounds.size.x, 0.001f, rel + ": one tile wide");
            Assert.AreEqual(64.5f, s.rect.height - s.pivot.y, 0.6f, rel + ": pivot at the top face's centre");
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(s));
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            Assert.AreEqual(SpriteMeshType.FullRect, settings.spriteMeshType, rel + ": full rect");
        }

        [Test]
        public void BlocksAndGroundTilesAreImportedToTheTemplates()
        {
            foreach (var b in Blocks) Check("Painted/Blocks/block_" + b, new Vector2(256, 260));
            foreach (var g in Ground) Check("Painted/Ground/ground_" + g, new Vector2(256, 152));
            foreach (var t in Town) Check("Tiles/tile_" + t, new Vector2(256, 152));
        }

        [Test]
        public void TheGameDoesNotOutlineThem()
        {
            var outlines = AssetDatabase.LoadAssetAtPath<Ravenholm.Presentation.OutlineSettings>(ProjectSetup.OutlineSettingsPath);
            foreach (var b in Blocks) Assert.IsFalse(outlines.IsOutlined(ProjectSetup.LoadSprite("Painted/Blocks/block_" + b)), b);
            foreach (var g in Ground) Assert.IsFalse(outlines.IsOutlined(ProjectSetup.LoadSprite("Painted/Ground/ground_" + g)), g);
        }

        private static readonly string[] Nature = { "boulder", "bush_big", "bush_small", "fern", "flowers", "grass_tuft", "leafy_tree", "log_fallen", "log_piece", "log_stack", "pine_bent", "pine_medium", "pine_small", "pine_tall", "rocks_small", "stump" };
        private static readonly string[] GameNature = { "tree_cedar", "tree_maple", "tree_ironwood", "tree_stump", "bush_herb", "rock_stone" };
        private static readonly string[] Buildings = { "bld_apothecary", "bld_forest_gate", "bld_forge", "bld_lumbermill", "bld_mine_mouth", "bld_smelter", "bld_store", "bld_trader_pad", "bld_workbench" };

        /// <summary>Astra's v4 nature and buildings: 256 px per unit (canvases doubled, so the same world size), full rect, outlined by the game.</summary>
        [Test]
        public void NatureAndBuildingsAreOutlinedAt256PixelsPerUnit()
        {
            var outlines = AssetDatabase.LoadAssetAtPath<Ravenholm.Presentation.OutlineSettings>(ProjectSetup.OutlineSettingsPath);
            void Check(string rel)
            {
                var s = ProjectSetup.LoadSprite(rel);
                Assert.NotNull(s, rel);
                Assert.AreEqual(256f, s.pixelsPerUnit, 0.01f, rel);
                Assert.IsTrue(outlines.IsOutlined(s), rel + ": the game's outline");
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(s));
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                Assert.AreEqual(SpriteMeshType.FullRect, settings.spriteMeshType, rel + ": full rect (room for the outline)");
            }
            foreach (var n in Nature) Check("Painted/Nature/" + n);
            foreach (var n in GameNature) Check("Nature/" + n);
            foreach (var b in Buildings) Check("Buildings/" + b);
            Assert.AreEqual(438f / 256f, ProjectSetup.LoadSprite("Nature/tree_cedar").bounds.size.x, 0.01f, "the tall pine keeps its world size");
        }
    }
}
