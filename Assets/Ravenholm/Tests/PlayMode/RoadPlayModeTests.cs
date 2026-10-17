using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Presentation;
using Ravenholm.Services;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The six levels in the real scene (gated levels, Kevin 2026-10-16): each laid as its own land when the company gets
    /// there (the camp in the same place at its front); Hollow Wood's gate is a ravine, a gap in the land until the bridge
    /// is built from stock at its sign, then walked across. Captures each level from the camp and from above.
    /// </summary>
    public sealed class RoadPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-road", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameRoot.Instance != null) Object.Destroy(GameRoot.Instance.gameObject);
            yield return null;
            yield return null;
            GameRoot.SaveDirectoryOverride = null;
            try { Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        private static IEnumerator Boot()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.NotNull(SandboxSceneController.Current);
            for (var i = 0; i < 3; i++) yield return null;
            GameRoot.Instance.Hud.Tips.HideAll();
        }

        private static IEnumerator JumpTo(int level)
        {
            GameRoot.Instance.Services.Camp.DebugJumpTo(level);
            yield return null;
            var deadline = Time.realtimeSinceStartup + 60f;
            while (DriveView.Current.Driving && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSeconds(0.6f);
        }

        /// <summary>The whole level from above, without the HUD (Captures/{name}.png).</summary>
        private static IEnumerator Overview(string name)
        {
            var cam = SandboxSceneController.Current.WorldCamera;
            var follow = cam.GetComponent<FollowCamera>();
            if (follow != null) follow.enabled = false;
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.enabled).ToList();
            foreach (var c in canvases) c.enabled = false;
            var (pos, size) = (cam.transform.position, cam.orthographicSize);
            var r = GameRoot.Instance.Services.Camp.Ridge;
            cam.transform.position = new Vector3((float)r.LevelCX, (float)r.LevelCY + 2f, pos.z);   // the outline's centre (world)
            cam.orthographicSize = (float)r.LevelRY * 1.9f;
            const int w = 1800, h = 1350;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            for (var i = 0; i < 4; i++) yield return null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            var dir = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Captures"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            cam.transform.position = pos;
            cam.orthographicSize = size;
            foreach (var c in canvases) c.enabled = true;
            if (follow != null) follow.enabled = true;
        }

        [UnityTest]
        public IEnumerator EachLevelIsLaidAsItsOwnLand()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            var ground = GameObject.Find("Grid").transform.Find("Ground").GetComponent<UnityEngine.Tilemaps.Tilemap>();
            var mine = MineSlabView.Current;
            for (var level = 0; level < 6; level++)
            {
                if (level > 0) yield return JumpTo(level);
                Assert.AreEqual(level, s.Camp.Index);
                Assert.AreEqual(level, LevelView.Current.Painted, $"level {level + 1}'s ground is laid");
                var r = s.Camp.Ridge;
                var e = r.Extent;
                var wrong = 0;
                for (var x = e.minX; x <= e.maxX; x++)
                for (var y = e.minY; y <= e.maxY; y++)
                {
                    var land = r.InLand(x, y) && (!r.InProjectGap(x, y) || s.Camp.IsBuilt(r.GateIndexAt(x, y)));
                    if (land != ground.HasTile(new Vector3Int(x, y, 0))) wrong++;
                }
                Assert.AreEqual(0, wrong, $"level {level + 1}: the ground is its land, cell for cell");
                Assert.IsTrue(mine.SurfaceWalkable(s.Camp.CentreOf(level)), $"level {level + 1}: the camp stands on land");
                Assert.IsTrue(mine.SurfaceWalkable(ShopView.Current.Wagon.position), $"level {level + 1}: so does the wagon");
                Assert.Less(Vector2.Distance(motor.transform.position, s.Camp.CentreOf(level)), 9f, $"level {level + 1}: the supplier is at the camp");
                Assert.AreEqual(level < 5, s.Camp.HasNext, level < 5 ? "a gate on to the next level" : "the last level: the castle ahead");
                yield return Capture.Take($"level{level + 1}_camp");
                yield return Overview($"level{level + 1}_overview");
            }
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }

        [UnityTest]
        public IEnumerator TheBridgeIsBuiltAndWalkedAcross()
        {
            yield return Boot();
            var root = GameRoot.Instance;
            var s = root.Services;
            var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            var mine = MineSlabView.Current;
            var spot = GateProjectSpot.Current;
            Assert.NotNull(spot, "the level's project sign is in the scene");
            Assert.IsFalse(spot.Shown, "level 1's gate is old trees: no sign");

            // Hollow Wood (level 3): its gate is the ravine, a gap until built.
            yield return JumpTo(2);
            Assert.IsTrue(spot.Shown, "the bridge's sign before the ravine");
            Assert.AreEqual(2, spot.Gate);
            Assert.Greater(spot.GapCells.Count, 10, "a ravine across the land");
            var mid = spot.GapCells.OrderBy(c => (c - spot.GapCells[spot.GapCells.Count / 2]).sqrMagnitude).First();
            var midWorld = IsoMap.World(mid);
            Assert.IsFalse(mine.SurfaceWalkable(midWorld), "nobody crosses the ravine yet");
            motor.Teleport(spot.transform.position + new Vector3(0.6f, -0.3f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.6f);
            Assert.AreSame(spot, SandboxSceneController.Current.ContextTarget, "the sign offers the bridge");
            yield return Capture.Take("road_ravine");
            spot.OnInspected();
            Assert.IsFalse(s.Camp.IsBuilt(2), "not without the timber and rope");

            s.Logistics.System.ProduceShared("resource.ironwood_lumber", 12, "test");
            s.Logistics.System.ProduceShared("resource.rope", 8, "test");
            spot.OnInspected();
            Assert.IsTrue(s.Camp.IsBuilt(2));
            yield return null;
            foreach (var c in spot.GapCells) Assert.IsTrue(mine.SurfaceWalkable(IsoMap.World(c)), $"plank at {c}");
            var forest = ForestGridView.Current;
            if (forest != null) Assert.IsTrue(forest.CanEnter(midWorld + new Vector2(-0.25f, -0.12f), midWorld), "no wall onto the bridge");
            motor.Teleport(midWorld);
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.6f);
            Assert.Less(Vector2.Distance(motor.transform.position, midWorld), 0.5f, "standing on the bridge, not sent back");
            yield return Capture.Take("road_bridge");

            // Witchlight Fen (level 4): the causeway's sign; the debug jump back to level 1 lays its ground again.
            yield return JumpTo(3);
            Assert.IsTrue(spot.Shown && spot.Gate == 3, "the causeway's sign");
            yield return Capture.Take("road_flood");
            yield return JumpTo(0);
            Assert.AreEqual(0, LevelView.Current.Painted);
            Assert.IsFalse(spot.Shown);
            Assert.Less(Vector2.Distance(motor.transform.position, s.Camp.CentreOf(0)), 9f, "back at Meadow's Edge");
            CollectionAssert.IsEmpty(s.Logistics.System.Audit());
        }
    }
}
