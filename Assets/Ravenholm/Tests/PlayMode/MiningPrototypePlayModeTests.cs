using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>The option-A digging prototype: two blocks tall, dig any neighbouring block, walk the tunnels you make.</summary>
    public sealed class MiningPrototypePlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-proto", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameRoot.Instance != null && GameRoot.Instance.InputRouter != null) GameRoot.Instance.InputRouter.ScriptedMove = null;
            if (GameRoot.Instance != null) Object.Destroy(GameRoot.Instance.gameObject);
            yield return null;
            yield return null;
            GameRoot.SaveDirectoryOverride = null;
            try { Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        private static IEnumerator Until(Func<bool> done, float seconds, string what)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(done(), what);
        }

        [UnityTest]
        public IEnumerator DigDownTunnelAcrossAndGoBack()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            yield return Until(() => GameRoot.Instance != null && SandboxSceneController.Current != null, 10f, "town loaded");
            for (var i = 0; i < 3; i++) yield return null;

            // The signpost by the Mine opens the prototype.
            var sign = Object.FindAnyObjectByType<PrototypeSign>();
            Assert.NotNull(sign, "a signpost by the Mine");
            sign.OnInspected();
            yield return Until(() => MiningPrototype.Current != null, 10f, "prototype loaded");
            yield return null;
            var p = MiningPrototype.Current;
            var router = GameRoot.Instance.InputRouter;
            Assert.IsFalse(GameRoot.Instance.Hud.gameObject.activeSelf, "the prototype has its own controls");
            Assert.AreEqual(0, p.Feet, "starts on the surface");

            // Dig straight down four blocks by tapping the one underfoot: the supplier drops with each one.
            for (var i = 0; i < 4; i++)
            {
                var under = new Vector3Int(0, Mathf.RoundToInt(p.U), p.Feet);
                yield return null;
                var spotDown = Camera.main.WorldToScreenPoint(p.Top(under.x, under.y, under.z) + new Vector3(0f, -0.3f, 0f));
                for (var tap = 0; tap < 4 && p.Selected != under; tap++) p.OnTapped(spotDown);
                Assert.AreEqual(under, p.Selected, "tapping the block underfoot selects it");
                p.ScriptedDig = true;
                var before = p.Feet;
                yield return Until(() => p.Feet > before, 5f, "digs down and drops");
                p.ScriptedDig = false;
            }
            Assert.AreEqual(4, p.Feet);
            p.ScriptedDig = true;
            // Then tunnel sideways (left on screen): two blocks high, so the supplier walks in.
            var startU = p.U;
            router.ScriptedMove = Vector2.left;
            yield return Until(() => p.U > startU + 3.5f, 20f, "tunnels along the face and walks into it");
            p.ScriptedDig = false;
            router.ScriptedMove = null;
            Assert.AreEqual(4, p.Feet, "a level tunnel: no falling, no climbing");
            Assert.GreaterOrEqual(p.DugCount, 4 + 6, "the shaft down and a 2-high tunnel");
            Assert.Greater(p.Tally.Values.Sum(), 0);
            Assert.IsFalse(p.Solid(Mathf.RoundToInt(p.U), p.Feet - 1) || p.Solid(Mathf.RoundToInt(p.U), p.Feet - 2), "the supplier stands in open tunnel");
            // Auto-target: the wall walked into (head height first, then legs), else the block underfoot.
            yield return null;
            {
                var lane0 = Mathf.RoundToInt(p.X);
                var col0 = Mathf.RoundToInt(p.U);
                var head = new Vector3Int(lane0 + p.Facing.x, col0 + p.Facing.y, p.Feet - 2);
                var legs = new Vector3Int(lane0 + p.Facing.x, col0 + p.Facing.y, p.Feet - 1);
                var under = new Vector3Int(lane0, col0, p.Feet);
                var expected = p.Diggable(head) ? head : p.Diggable(legs) ? legs : under;
                Assert.IsNull(p.Selected);
                Assert.AreEqual(expected, p.Target, "head height first, then legs, else the block underfoot");
            }

            // Depth: the floor is drawn behind the supplier, the rock over their head (nearer the camera) in front.
            var col = Mathf.RoundToInt(p.U);
            yield return null;
            Assert.Less(p.DrawnOrder(new Vector3Int(0, col, p.Feet)), p.PlayerOrder, "the floor block is behind the supplier");
            Assert.Greater(p.DrawnOrder(new Vector3Int(0, col, p.Feet - 3)), p.PlayerOrder, "the block over the head covers them");

            // What can be dug wears a faint outline; pressing and holding on one digs it without the Dig button.
            Assert.Greater(p.OutlinedBlocks, 0, "reachable blocks are outlined");
            var ahead = new Vector3Int(0, col + 1, p.Feet - 1);
            Assert.IsTrue(p.Diggable(ahead) && p.Reachable().Contains(ahead), "the block ahead in the tunnel is reachable");
            router.ScriptedPress = Camera.main.WorldToScreenPoint(p.Top(ahead.x, ahead.y, ahead.z) + new Vector3(0f, -0.25f, 0f));
            yield return Until(() => !p.Diggable(ahead), 5f, "press and hold digs the block under the finger");
            router.ScriptedPress = null;

            // Dig inwards: tap the blocks behind the tunnel (body and head height), dig them, and walk into the cliff.
            for (var lane = 1; lane <= 2; lane++)
            {
                foreach (var row in new[] { p.Feet - 1, p.Feet - 2 })
                {
                    var pick = new Vector3Int(lane, col, row);
                    Assert.IsTrue(p.Diggable(pick), $"rock at {pick}");
                    var spotIn = Camera.main.WorldToScreenPoint(p.Top(lane, col, row) + new Vector3(-0.15f, -0.35f, 0f));   // its left face
                    // Nothing is hidden: where blocks overlap on screen, tapping again steps to the one behind.
                    for (var tap = 0; tap < 5 && p.Selected != pick; tap++) p.OnTapped(spotIn);
                    Assert.AreEqual(pick, p.Selected, $"tapping selects the block there ({pick})");
                    yield return null;
                    Assert.AreEqual(pick, p.Target, "and it becomes the block to dig");
                    p.ScriptedDig = true;
                    yield return Until(() => !p.Diggable(pick), 5f, "the selected block is dug");
                    p.ScriptedDig = false;
                    Assert.IsNull(p.Selected, "the selection clears once dug");
                }
                router.ScriptedMove = new Vector2(2f, 1f).normalized;   // screen up-right: straight into the cliff
                var into = lane;
                yield return Until(() => p.X > into - 0.25f, 5f, $"walks into the cliff (lane {lane})");
                router.ScriptedMove = null;
                yield return null;
            }
            Assert.AreEqual(4, p.Feet, "still on the same level");
            Assert.AreEqual(0, p.FadedBlocks, "see-through is off by default: nothing is hidden");
            p.CutawayOn = true;
            yield return null;
            Assert.Greater(p.FadedBlocks, 0, "switched on, the rock in front turns see-through");
            // The walls right beside the supplier stay visible as ghosts and can be tapped: they are what is in the way.
            var lane2 = Mathf.RoundToInt(p.X);
            var wall = new Vector3Int(lane2, col - 1, p.Feet - 1);   // the side wall towards the camera, at body height
            Assert.IsTrue(p.Diggable(wall));
            Assert.IsTrue(p.Ghost(wall.x, wall.y, wall.z), "a cut-away wall touching the supplier is ghosted, not hidden");
            Assert.Greater(p.GhostBlocks, 0);
            var spot = Camera.main.WorldToScreenPoint(p.Top(wall.x, wall.y, wall.z) + new Vector3(-0.15f, -0.35f, 0f));
            for (var tap = 0; tap < 5 && p.Selected != wall; tap++) p.OnTapped(spot);   // tapping again steps through what is there
            Assert.AreEqual(wall, p.Selected, "a ghosted wall can be selected (tapping the spot again steps to it) and dug");
            Assert.IsFalse(p.InReach(new Vector3Int(0, col + 6, 2)), "blocks far away are out of reach");

            yield return new WaitForSeconds(0.6f);
            yield return Capture.Take("mining_prototype");

            p.BackToTown();
            yield return Until(() => SandboxSceneController.Current != null, 10f, "back in town");
            Assert.IsTrue(GameRoot.Instance.Hud.gameObject.activeSelf);
        }
    }
}
