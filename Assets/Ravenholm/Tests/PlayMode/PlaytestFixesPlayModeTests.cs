using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The playtester's findings, fixed (2026-10-17): trees in front of the supplier fade as buildings do; the camp's
    /// message fits its box below the debug banner.
    /// </summary>
    public sealed class PlaytestFixesPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-fixes", Guid.NewGuid().ToString("N"));
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

        [UnityTest]
        public IEnumerator TreesFadeInFrontOfTheSupplierAndTheMessageFitsItsBox()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            for (var i = 0; i < 3; i++) yield return null;
            var root = GameRoot.Instance;
            root.Hud.Tips.HideAll();
            var forest = ForestGridView.Current;
            var sim = root.Services.Sites.Get("site.forest");
            var tree = CampPlayModeTests.TreeAhead(sim, 10);
            Assert.IsTrue(forest.CellOf(tree.x, tree.y, out var cell));
            var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            // Just behind the tree (higher on the screen), where its crown covers him.
            motor.Teleport((Vector2)forest.RendererAt(cell).transform.position + new Vector2(0f, 0.3f));
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(forest.TreeFaded(cell), "the tree in front of him fades");
            yield return Capture.Take("tree_fade");
            motor.Teleport((Vector2)forest.RendererAt(cell).transform.position + new Vector2(0f, -1.5f));   // in front of it now
            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(forest.TreeFaded(cell), "and comes back when he steps out");

            // The long camp message: inside its box, below the banner.
            root.Hud.ShowToast("Camp made at Hollow Wood. Set your stations up from their kits (Build).");
            yield return new WaitForSeconds(0.3f);
            var toast = GameObject.Find("Toast").GetComponent<RectTransform>();
            var text = toast.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            Assert.LessOrEqual(text.preferredHeight, toast.rect.height, "the text fits inside its box");
            var banner = GameObject.Find("PhaseBanner");
            if (banner != null && banner.activeInHierarchy)
            {
                var b = banner.GetComponent<RectTransform>();
                var bc = new Vector3[4];
                var tc = new Vector3[4];
                b.GetWorldCorners(bc);
                toast.GetWorldCorners(tc);
                Assert.LessOrEqual(tc[1].y, bc[0].y + 0.5f, "the message starts below the banner");
            }
            yield return Capture.Take("toast_fits");
        }

        [UnityTest]
        public IEnumerator FacingARockBesideATreeDigsAndTheForgePanelKeepsItsCloseInView()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            for (var i = 0; i < 3; i++) yield return null;
            var root = GameRoot.Instance;
            root.Hud.Tips.HideAll();
            var forest = ForestGridView.Current;
            var sim = root.Services.Sites.Get("site.forest");
            var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();

            // A hill block with flat ground beside it, and a standing tree also beside that ground (not in front).
            var c0 = IsoMap.Cell(motor.transform.position);
            Vector3Int[] four = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };
            var found = false;
            Vector3Int rock = default, stand = default;
            for (var r = 2; r < 40 && !found; r++)
            for (var x = c0.x - r; x <= c0.x + r && !found; x++)
            for (var y = c0.y - r; y <= c0.y + r && !found; y++)
            {
                var c = new Vector3Int(x, y, 0);
                if (forest.HeightAt(c) < 2 || root.Services.Sites.ToolTooWeak(x, y)) continue;
                foreach (var n in four)
                {
                    var st = c + n;
                    if (forest.HeightAt(st) != 0 || !MineSlabView.Current.SurfaceWalkable(IsoMap.World(st)) || sim.CanChop(st.x, st.y)) continue;
                    foreach (var m in four)
                    {
                        var t = st + m;
                        if (t == c || !sim.CanChop(t.x, t.y) || forest.HeightAt(t) != 0) continue;
                        rock = c; stand = st; found = true; break;
                    }
                    if (found) break;
                }
            }
            Assert.IsTrue(found, "a hill block at the forest's edge with a tree beside");
            motor.Teleport(IsoMap.World(stand));
            yield return null;
            var d = (Vector2)IsoMap.World(rock) - (Vector2)motor.transform.position;
            root.InputRouter.ScriptedMove = new Vector2(d.x, d.y * 2f).normalized * 0.3f;   // face the rock
            yield return new WaitForSeconds(0.1f);
            root.InputRouter.ScriptedMove = null;
            motor.Teleport(IsoMap.World(stand));
            yield return new WaitForSeconds(0.4f);
            Assert.IsInstanceOf<ForestRockSpot>(SandboxSceneController.Current.ContextTarget, $"facing the rock the button digs (it says \"{root.Hud.ContextLabel}\")");
            yield return Capture.Take("dig_beside_tree");

            // The Forge's panel: Close pinned at the card's foot, a hint when the list runs on below.
            var forge = GameObject.Find("Forge").GetComponent<StationInspectable>();
            SandboxSceneController.Current.Inspect(forge);
            yield return new WaitForSeconds(0.4f);
            var panel = root.Hud.StationPanel;
            Assert.IsTrue(panel.IsOpen);
            var close = panel.transform.Find("Card/Close");
            Assert.NotNull(close, "a Close button pinned to the card");
            var card = panel.transform.Find("Card").GetComponent<RectTransform>();
            var cc = new Vector3[4];
            var bc = new Vector3[4];
            card.GetWorldCorners(cc);
            close.GetComponent<RectTransform>().GetWorldCorners(bc);
            Assert.GreaterOrEqual(bc[0].y, cc[0].y, "inside the card, at its foot");
            var content = panel.transform.Find("Card/Viewport/Content").GetComponent<RectTransform>();
            var view = panel.transform.Find("Card/Viewport").GetComponent<RectTransform>();
            if (content.rect.height > view.rect.height + 4f) Assert.IsTrue(panel.MoreBelow, "the list runs on: the hint says so");
            yield return Capture.Take("forge_panel_close");
            panel.Close();
        }
    }
}