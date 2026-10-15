using System.Collections.Generic;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Astra's 41 carried-item sprites (2026-10-14): every carriable material and crafted item resolves to its own
    /// stack_&lt;id&gt; sprite, imported whole (256 x 192 at 512 px per unit, pivot on the floor line, full-rect mesh), with
    /// its pile spacing from the art's spacing file.
    /// </summary>
    public sealed class StackSpriteTests
    {
        private static ContentCatalogue Catalogue => AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);

        private static IEnumerable<(string id, Sprite sprite, float step)> Carriables()
        {
            var c = Catalogue;
            foreach (var r in c.Resources) yield return (r.Id, r.StackSprite, r.StackStep);
            foreach (var i in c.Items) yield return (i.Id, i.StackSprite, i.StackStep);
        }

        [Test]
        public void EveryCarriableResolvesToItsOwnStackSprite()
        {
            var seen = new HashSet<Sprite>();
            var count = 0;
            foreach (var (id, sprite, _) in Carriables())
            {
                count++;
                Assert.NotNull(sprite, $"{id} has a stack sprite");
                var suffix = id.Substring(id.IndexOf('.') + 1);
                Assert.AreEqual("stack_" + suffix, sprite.name, $"{id} uses its own art");
                Assert.IsTrue(seen.Add(sprite), $"{id}: no two items share a sprite");
            }
            Assert.AreEqual(48, count, "19 materials and 29 crafted items (with the helmets and shields)");
        }

        [Test]
        public void StackSpritesAreImportedWholeWithThePivotOnTheFloorLine()
        {
            foreach (var (id, sprite, _) in Carriables())
            {
                Assert.AreEqual(512f, sprite.pixelsPerUnit, 0.01f, $"{id}: 512 px per unit");
                Assert.AreEqual(new Vector2(256, 192), sprite.rect.size, $"{id}: the full canvas, untrimmed");
                Assert.AreEqual(128f, sprite.pivot.x, 0.5f, $"{id}: pivot centred");
                Assert.AreEqual(16f, sprite.pivot.y, 0.5f, $"{id}: pivot 16 px up (1/12)");
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite));
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                Assert.AreEqual(SpriteMeshType.FullRect, settings.spriteMeshType, $"{id}: full-rect mesh");
                Assert.IsFalse(importer.mipmapEnabled, $"{id}: no mipmaps");
                Assert.AreEqual(FilterMode.Bilinear, importer.filterMode, $"{id}: bilinear");
                Assert.AreEqual(TextureWrapMode.Clamp, importer.wrapMode, $"{id}: clamp");
                // The size on screen is unchanged from the placeholders: 0.5 x 0.375 units before the pile's scale.
                Assert.AreEqual(0.5f, sprite.bounds.size.x, 0.001f, id);
            }
        }

        [Test]
        public void ThinPiecesStackCloserThanBulkyOnes()
        {
            var steps = new Dictionary<string, float>();
            foreach (var (id, _, step) in Carriables()) steps[id.Substring(id.IndexOf('.') + 1)] = step;
            Assert.AreEqual(0.13f, steps["iron_ore"], 0.0001f, "bulky pieces keep the full step");
            Assert.AreEqual(0.04805f, steps["maple_longbow"], 0.0001f, "a bow lies flat: from the spacing file");
            Assert.Less(steps["cedar_lumber"], 0.1f, "planks stack close");
            foreach (var kv in steps) Assert.That(kv.Value, Is.InRange(0.04f, 0.13f), kv.Key);
        }
    }
}
