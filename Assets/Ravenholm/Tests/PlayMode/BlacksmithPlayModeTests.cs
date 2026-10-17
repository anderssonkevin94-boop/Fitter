using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Astra's modular Blacksmith (Kevin, 2026-10-17: one setup): the furnace (the Smelter) and the crafting station (the
    /// Forge) on one rug, each at its own level with its own art, each fire burning while it works and out when idle, both
    /// at once; the rug and mats walked on, the two stations solid.
    /// </summary>
    public sealed class BlacksmithPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-smithy", Guid.NewGuid().ToString("N"));
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

        private static IEnumerator Look(Vector2 at, string name)
        {
            var cam = SandboxSceneController.Current.WorldCamera;
            var follow = cam.GetComponent<FollowCamera>();
            if (follow != null) follow.enabled = false;
            var (pos, size) = (cam.transform.position, cam.orthographicSize);
            cam.transform.position = new Vector3(at.x, at.y + 0.6f, pos.z);
            cam.orthographicSize = 2.6f;
            yield return null;
            yield return Capture.Take(name);
            cam.transform.position = pos;
            cam.orthographicSize = size;
            if (follow != null) follow.enabled = true;
        }

        [UnityTest]
        public IEnumerator BothFiresBurnWhileTheyWorkAndEachUpgradesOnItsOwn()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            for (var i = 0; i < 3; i++) yield return null;
            var root = GameRoot.Instance;
            root.Hud.Tips.HideAll();
            var s = root.Services;
            var rug = GameObject.Find("Blacksmith").transform;
            var furnace = rug.Find("Smelter").GetComponent<StationArt>();
            var anvil = rug.Find("Forge").GetComponent<StationArt>();
            Assert.NotNull(furnace, "the furnace is the Smelter");
            Assert.NotNull(anvil, "the crafting station is the Forge");
            var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            motor.Teleport((Vector2)rug.position + new Vector2(0.2f, -1.6f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.4f);
            // Standing in front of the Forge, the button opens the Forge (not the pickup mat beside it; playtest, 2026-10-17).
            motor.Teleport((Vector2)anvil.transform.position + new Vector2(0f, -1.1f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.4f);
            Assert.AreSame(anvil.GetComponent<StationInspectable>(), SandboxSceneController.Current.ContextTarget,
                $"at the Forge the button offers the Forge (it says \"{root.Hud.ContextLabel}\")");
            root.Hud.ScriptedPress();
            yield return null;
            Assert.IsTrue(root.Hud.StationPanel.IsOpen, "tapping it opens the Forge's crafting panel");
            root.Hud.StationPanel.Close();
            motor.Teleport((Vector2)rug.position + new Vector2(0.2f, -1.6f));
            SandboxSceneController.Current.SnapPlayerToSavedPosition();
            yield return new WaitForSeconds(0.4f);
            Assert.IsFalse(furnace.Burning, "idle: the furnace is out");
            Assert.IsFalse(anvil.Burning, "idle: the coals are out");
            yield return Look(rug.position, "blacksmith_idle");

            // The rug and the mats are walked on; the stations block the way.
            var mine = MineSlabView.Current;
            var mask = LayerMask.GetMask(PlayerMotor.BlockerLayerName);
            Assert.IsNull(Physics2D.OverlapPoint(rug.position, mask), "the aisle down the rug's middle is open");
            Assert.NotNull(Physics2D.OverlapPoint(furnace.transform.position, mask), "the furnace is solid");
            Assert.NotNull(Physics2D.OverlapPoint(anvil.transform.position, mask), "the anvil is solid");
            Assert.IsTrue(mine.SurfaceWalkable(rug.position), "the rug lies on walkable ground");

            // Smelting and crafting at the same time: both fires burn.
            var sys = s.Logistics.System;
            foreach (var (id, n) in new[] { ("resource.iron_ore", 12), ("resource.coal", 12), ("resource.iron_ingot", 12), ("resource.cedar_lumber", 6), ("resource.fur", 6), ("resource.copper_ingot", 6) })
                sys.ProduceShared(id, n, "test");
            Assert.IsTrue(s.Production.SelectProcess("station.smelter", "process.smelt_iron"));
            Assert.IsTrue(s.Production.Craft("recipe.iron_sword", out var why), why);
            yield return new WaitForSeconds(1.2f);
            Assert.IsTrue(furnace.Burning, "smelting: the furnace burns");
            Assert.IsTrue(anvil.Burning, "crafting: the coals glow");
            yield return Look(rug.position, "blacksmith_working");

            // Each upgrades on its own: the furnace to 3 while the anvil stays at 1, then the anvil to 2.
            var smelterDef = s.Catalogue.Get<StationDefinition>("station.smelter");
            var forgeDef = s.Catalogue.Get<StationDefinition>("station.forge");
            while (s.Production.System.Level("station.smelter") < 3) s.Production.DebugCycleLevel(smelterDef);
            yield return null;
            Assert.AreEqual(3, furnace.ShownLevel);
            Assert.AreEqual(1, anvil.ShownLevel);
            Assert.AreEqual("furnace_3", rug.Find("Smelter").GetComponent<SpriteRenderer>().sprite.name);
            Assert.AreEqual("crafting_1", rug.Find("Forge").GetComponent<SpriteRenderer>().sprite.name);
            s.Production.DebugCycleLevel(forgeDef);
            yield return null;
            Assert.AreEqual(2, anvil.ShownLevel);
            Assert.AreEqual("crafting_2", rug.Find("Forge").GetComponent<SpriteRenderer>().sprite.name);
            Assert.AreEqual((Vector2)rug.position + new Vector2(-0.225f, 0.5375f), (Vector2)furnace.transform.position, "upgrades stay in place");
            yield return Look(rug.position, "blacksmith_levels_3_2");
            CollectionAssert.IsEmpty(sys.Audit());
        }
    }
}
