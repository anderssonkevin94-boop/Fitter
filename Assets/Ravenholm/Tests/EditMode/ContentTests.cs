using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.EditorTools;
using Ravenholm.Presentation;
using UnityEditor;
using UnityEngine;

namespace Ravenholm.Tests
{
    public sealed class CatalogueTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private Sprite DummySprite()
        {
            var tex = new Texture2D(2, 2);
            _created.Add(tex);
            var s = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.zero);
            _created.Add(s);
            return s;
        }

        private ResourceDefinition Resource(string id, string name = "Thing")
        {
            var r = ScriptableObject.CreateInstance<ResourceDefinition>();
            r.name = id;
            r.EditorInitialize(id, name);
            r.EditorSetup(ResourceCategory.Processed, ReceiverType.None, DummySprite(), DummySprite());
            _created.Add(r);
            return r;
        }

        private ItemDefinition Sword(string id)
        {
            var i = ScriptableObject.CreateInstance<ItemDefinition>();
            i.name = id;
            i.EditorInitialize(id, "Sword");
            i.EditorSetup(ItemCategory.Weapon, 8, 0, WeaponTag.Blade, PotionEffect.None, 0, 2, DummySprite(), DummySprite());
            _created.Add(i);
            return i;
        }

        private ContentCatalogue Catalogue(List<ResourceDefinition> r, List<ItemDefinition> i)
        {
            var c = ScriptableObject.CreateInstance<ContentCatalogue>();
            c.EditorSet(r, i);
            _created.Add(c);
            return c;
        }

        [Test]
        public void DetectsDuplicateIdsAcrossDifferentAssets()
        {
            var c = Catalogue(new List<ResourceDefinition> { Resource("resource.iron_ore"), Resource("resource.iron_ore") }, new List<ItemDefinition>());
            var report = c.Validate();
            Assert.IsFalse(report.IsValid);
            Assert.IsTrue(report.Errors.Any(e => e.Contains("Duplicate ID 'resource.iron_ore'")), report.ToString());
        }

        [Test]
        public void DetectsSameAssetListedTwice()
        {
            var r = Resource("resource.iron_ore");
            var report = Catalogue(new List<ResourceDefinition> { r, r }, new List<ItemDefinition>()).Validate();
            Assert.IsTrue(report.Errors.Any(e => e.Contains("listed twice")), report.ToString());
        }

        [Test]
        public void DetectsMissingReferenceInvalidIdAndWrongKind()
        {
            var c = Catalogue(new List<ResourceDefinition> { null, Resource("resource.iron ore") }, new List<ItemDefinition> { Sword("resource.sword") });
            var report = c.Validate();
            Assert.IsTrue(report.Errors.Any(e => e.Contains("missing reference")), report.ToString());
            Assert.IsTrue(report.Errors.Any(e => e.Contains("invalid character")), report.ToString());
            Assert.IsTrue(report.Errors.Any(e => e.Contains("must start with 'item.'")), report.ToString());
        }

        [Test]
        public void DetectsInconsistentItemFields()
        {
            var bad = ScriptableObject.CreateInstance<ItemDefinition>();
            _created.Add(bad);
            bad.EditorInitialize("item.bad_potion", "Bad Potion");
            bad.EditorSetup(ItemCategory.Potion, 5, 0, WeaponTag.None, PotionEffect.None, 0, 1, DummySprite(), DummySprite());
            var report = Catalogue(new List<ResourceDefinition>(), new List<ItemDefinition> { bad }).Validate();
            Assert.IsTrue(report.Errors.Any(e => e.Contains("potion needs one effect")), report.ToString());
            Assert.IsTrue(report.Errors.Any(e => e.Contains("weapon/armor fields")), report.ToString());
        }

        [Test]
        public void LookupByIdAndUnknownIdFailsLoudly()
        {
            var c = Catalogue(new List<ResourceDefinition> { Resource("resource.iron_ore") }, new List<ItemDefinition> { Sword("item.iron_sword") });
            Assert.IsTrue(c.TryGet<ItemDefinition>("item.iron_sword", out var sword));
            Assert.AreEqual(8, sword.Power);
            Assert.IsFalse(c.TryGet<ResourceDefinition>("item.iron_sword", out _), "type-checked lookup");
            Assert.Throws<KeyNotFoundException>(() => c.Get<ItemDefinition>("item.unknown"));
        }

        [Test]
        public void ProjectCatalogueIsValidAndComplete()
        {
            var report = ContentValidationMenu.ValidateProjectCatalogue();
            Assert.IsTrue(report.IsValid, report.ToString());
            var c = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            Assert.AreEqual(21, c.Resources.Count);   // 15 + moonsilver and ironwood (raw and processed), Phase 7; berries and mushrooms (2026-10-16)
            Assert.GreaterOrEqual(c.Items.Count, 6, "the six GDD starter items, plus later additions");
            foreach (var key in new[] { "iron_sword", "cedar_bow", "iron_dagger", "cedar_staff", "riveted_coat", "recovery_tonic" })
                Assert.IsTrue(c.TryGet<ItemDefinition>("item." + key, out _), key);
            // One accessible starter weapon per weapon family the four classes use (GDD 9).
            foreach (var tag in new[] { WeaponTag.Blade, WeaponTag.Bow, WeaponTag.LightBlade, WeaponTag.Staff })
                Assert.IsTrue(c.Items.Any(i => i.Category == ItemCategory.Weapon && i.WeaponTag == tag), $"no starter weapon for {tag}");
        }

        [Test]
        public void BootConfigIsWired()
        {
            var boot = AssetDatabase.LoadAssetAtPath<BootConfig>(ProjectSetup.ConfigDir + "BootConfig.asset");
            Assert.NotNull(boot);
            Assert.NotNull(boot.Catalogue);
            Assert.NotNull(boot.SandboxSettings);
            Assert.NotNull(boot.UiTheme);
            Assert.NotNull(boot.Balance);
            Assert.AreNotEqual("main", boot.SaveProfile, "the sandbox must not write to the production profile");
        }

        [Test]
        public void ScenesReferenceTheirConfigAndTiles()
        {
            // Guards against scenes saved with silently-null references.
            var bootDeps = AssetDatabase.GetDependencies(ProjectSetup.BootstrapScene, false);
            CollectionAssert.Contains(bootDeps, ProjectSetup.ConfigDir + "BootConfig.asset");
            var sandboxDeps = AssetDatabase.GetDependencies(ProjectSetup.SandboxScene, false);
            CollectionAssert.Contains(sandboxDeps, ProjectSetup.TileDir + "tile_grass_a.asset");
            CollectionAssert.Contains(sandboxDeps, PlaceholderSpriteImporter.ArtRoot + "Shop/wall_plain_left.png");
        }

        [Test]
        public void BuildSettingsStartWithBootstrap()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.GreaterOrEqual(scenes.Length, 2);
            Assert.AreEqual(ProjectSetup.BootstrapScene, scenes[0].path);
            Assert.IsTrue(scenes.Any(s => s.path == ProjectSetup.SandboxScene));
        }
    }

    public sealed class CameraFramingTests
    {
        [TestCase(1206, 2622)]   // iPhone 16 Pro
        [TestCase(750, 1334)]    // iPhone SE (16:9)
        public void OrthoSizeShowsRequestedWidthAtAnyPortraitAspect(int w, int h)
        {
            var aspect = (float)w / h;
            var size = FollowCamera.OrthoSizeForWidth(8.5f, aspect);
            Assert.AreEqual(8.5f, size * 2f * aspect, 1e-4f);
        }
    }
}
