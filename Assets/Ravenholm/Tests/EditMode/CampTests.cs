using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;
using UnityEngine;

namespace Ravenholm.Tests
{
    /// <summary>Camp 1 (the journey, 2026-10-15): the forest and hills wrap the clearing; further out is tougher and deeper.</summary>
    public sealed class CampTests
    {
        private static ForestGrove Ring() => SitesService.RingOf(Camp(), new ForestGrove());

        private static CampDefinition Camp()
        {
            var camp = ScriptableObject.CreateInstance<CampDefinition>();
            camp.EditorSetup(1, new RectInt(-20, -16, 48, 37), 20, 7, 3);
            return camp;
        }

        [Test]
        public void TheForestWrapsTheClearingsLowSidesAndTips()
        {
            var g = Ring();
            Assert.IsTrue(g.InGrove(-21, 0), "beyond the lower-left edge");
            Assert.IsTrue(g.InGrove(0, -17), "beyond the lower-right edge");
            Assert.IsTrue(g.InGrove(-40, 20), "around the left tip, to the cliff");
            Assert.IsTrue(g.InGrove(27, -36), "around the right tip, to the cliff");
            Assert.IsTrue(g.InGrove(-40, -36), "the far corner");
            Assert.IsFalse(g.InGrove(0, 0), "the clearing");
            Assert.IsFalse(g.InGrove(-21, 21), "past the cliff (upper-left)");
            Assert.IsFalse(g.InGrove(28, 0), "past the cliff (upper-right)");
            Assert.IsFalse(g.InGrove(-41, 0), "past the camp's edge");
        }

        [Test]
        public void BandsRunOutwardAndTheGroundRisesPastAFlatMargin()
        {
            var g = Ring();
            Assert.AreEqual(1, g.BandAt(-21, 0));
            Assert.AreEqual(1, g.BandAt(-27, 0));
            Assert.AreEqual(2, g.BandAt(-28, 0));
            Assert.AreEqual(3, g.BandAt(-40, -36));
            Assert.AreEqual(g.BandAt(-30, 5), g.BandAt(5, -26), "the same distance out on either side is the same band");
            for (var y = -10; y < 10; y++) Assert.AreEqual(0, g.TerrainHeightAt(42, -21, y), "flat beside the clearing");
            var hills = 0;
            for (var x = -40; x < -26; x++)
            for (var y = -10; y < 10; y++) if (g.TerrainHeightAt(42, x, y) > 0) hills++;
            Assert.Greater(hills, 10, "hills further out");
        }

        [Test]
        public void TheCampsMineHasNoQuarryAndItsLayersLieFurtherOut()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            Assert.NotNull(cat.CurrentCamp, "camp 1 is in the catalogue");
            var mine = cat.Get<SiteDefinition>("site.mine");
            var cfg = SitesService.BuildConfig(mine, cat.CurrentCamp);
            Assert.AreEqual(0, cfg.Dig.Length, "no quarry plot");
            var forest = SitesService.BuildConfig(cat.Get<SiteDefinition>("site.forest"), cat.CurrentCamp);
            Assert.IsTrue(forest.Grove.Ring);
            Assert.AreEqual(cat.CurrentCamp.Clearing.xMin, forest.Grove.ClearX0);
        }
    }
}
