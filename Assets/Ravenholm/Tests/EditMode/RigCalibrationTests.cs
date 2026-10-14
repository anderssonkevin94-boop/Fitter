using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The supplier's calibrated joints against his art (Docs/ANIMATION_SPEC.md, section 2): shoulders at the top of the
    /// shoulder line, resting hands just outside his outline in the front, back and diagonal views (not resting on his
    /// apron, which reads as arms held behind him).
    /// </summary>
    public sealed class RigCalibrationTests
    {
        private const string Dir = "Assets/Ravenholm/Art/Placeholder/Characters/StoreOwner/";

        private static Texture2D Body(string view)
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(Dir + "body_" + view + ".png"));
            return tex;
        }

        private static bool Opaque(Texture2D t, int x, int yTop) => t.GetPixel(x, t.height - 1 - yTop).a > 0.5f;

        [TestCase("S")] [TestCase("SE")] [TestCase("NE")] [TestCase("N")]
        public void RestingHandsHangOutsideTheBody(string view)
        {
            var cal = JObject.Parse(File.ReadAllText(Dir + "rig_calibration.json"))[view];
            var tex = Body(view);
            foreach (var side in new[] { "left", "right" })
            {
                var w = cal["wrist_" + side];
                int x = (int)w[0], y = (int)w[1];
                Assert.IsFalse(Opaque(tex, x, y), $"{view} {side} wrist is off the body");
                // and the hand (about 20 px wide) clears the outline toward the body too
                var toward = x < 256 ? 1 : -1;
                Assert.IsFalse(Opaque(tex, x + toward * 8, y), $"{view} {side} hand clears the outline");
            }
        }

        [TestCase("S")] [TestCase("SE")] [TestCase("E")] [TestCase("NE")] [TestCase("N")]
        public void ShouldersSitAtTheShoulderLine(string view)
        {
            var cal = JObject.Parse(File.ReadAllText(Dir + "rig_calibration.json"))[view];
            foreach (var side in new[] { "left", "right" })
            {
                var y = (int)cal["shoulder_" + side][1];
                Assert.That(y, Is.InRange(238, 258), $"{view} {side}: within the top of the sleeve (shirt line at about 225-237 px), not mid-arm");
            }
        }
    }
}
