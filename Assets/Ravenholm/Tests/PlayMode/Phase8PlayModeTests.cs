using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>Phase 8 usability checks in the real scene: text scaling, touch targets, occlusion, toggles, tips.</summary>
    public sealed class Phase8PlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-phase8", Guid.NewGuid().ToString("N"));
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
            yield return null;
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.NotNull(SandboxSceneController.Current);
            for (var i = 0; i < 3; i++) yield return null;
        }

        /// <summary>Visible texts whose wrapped content is taller than their box (auto-sized texts shrink instead).</summary>
        private static List<string> Overflowing(Transform root)
        {
            Canvas.ForceUpdateCanvases();
            var bad = new List<string>();
            foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(false))
            {
                if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.enableAutoSizing) continue;
                var r = t.rectTransform.rect;
                if (r.width <= 1f) continue;
                var need = t.GetPreferredValues(t.text, r.width, 0f).y;
                if (need > r.height + 4f) bad.Add($"{t.transform.parent?.name}/{t.name} needs {need:0} has {r.height:0}: \"{Clip(t.text)}\"");
            }
            return bad;
        }

        private static string Clip(string s) => s.Length > 40 ? s.Substring(0, 40) + "…" : s;

        [UnityTest]
        public IEnumerator LargestTextSizeFitsEveryPanel()
        {
            yield return Boot();
            var root = GameRoot.Instance;
            var hud = root.Hud;
            root.Settings.Update(s => s.TextScale = 1.3f);
            yield return null;
            yield return null;
            var problems = new List<string>();
            problems.AddRange(Overflowing(hud.transform).Select(x => "HUD: " + x));

            var opens = new (string name, Action open, Action close)[]
            {
                ("Job Board", hud.ShowJobBoard, () => hud.JobBoard.Close()),
                ("Smelter", () => SandboxSceneController.Current.Inspect(GameObject.Find("Smelter").GetComponent<StationInspectable>()), () => hud.StationPanel.Close()),
                ("Store", hud.ShowStore, () => hud.StorePanel.Close()),
                ("Trader", hud.ShowTrader, () => hud.TraderPanel.Close()),
                ("Settings", hud.ShowSettings, () => hud.SettingsPanel.Close()),
            };
            foreach (var (name, open, close) in opens)
            {
                open();
                yield return new WaitForSeconds(0.35f);
                problems.AddRange(Overflowing(hud.transform).Select(x => name + ": " + x));
                if (name == "Job Board") yield return Capture.Take("phase8_text_130_job_board");
                close();
                yield return null;
            }
            CollectionAssert.IsEmpty(problems, string.Join("\n", problems));
        }

        [UnityTest]
        public IEnumerator EveryButtonMeetsTheTouchTarget()
        {
            yield return Boot();
            var hud = GameRoot.Instance.Hud;
            var minimum = GameRoot.Instance.Config.UiTheme.minTouchTarget;   // 132 canvas units ≈ 44 pt on the reference phone
            var small = new List<string>();
            void Check()
            {
                Canvas.ForceUpdateCanvases();
                foreach (var b in hud.GetComponentsInChildren<Button>(false))
                {
                    var r = ((RectTransform)b.transform).rect;
                    if (Mathf.Min(r.width, r.height) < minimum * 0.6f) small.Add($"{b.name} {r.width:0}x{r.height:0}");
                }
            }
            Check();
            hud.ShowJobBoard(); yield return new WaitForSeconds(0.3f); Check(); hud.JobBoard.Close();
            hud.ShowSettings(); yield return new WaitForSeconds(0.3f); Check(); hud.SettingsPanel.Close();
            CollectionAssert.IsEmpty(small, "buttons under ~26 pt in either dimension:\n" + string.Join("\n", small));
        }

        [UnityTest]
        public IEnumerator NoInteractiveTargetIsFullyOccluded()
        {
            yield return Boot();
            var c = SandboxSceneController.Current;
            var hidden = new List<string>();
            foreach (var insp in Object.FindObjectsByType<Inspectable>(FindObjectsInactive.Exclude))
            {
                var col = insp.GetComponent<Collider2D>();
                if (!insp.isActiveAndEnabled || col == null || !col.enabled || insp.transform == c.Player) continue;
                var b = col.bounds;
                var reachable = false;
                for (var i = 1; i < 6 && !reachable; i++)
                    for (var j = 1; j < 6 && !reachable; j++)
                        reachable = c.FrontmostAt(new Vector2(Mathf.Lerp(b.min.x, b.max.x, i / 6f), Mathf.Lerp(b.min.y, b.max.y, j / 6f))) == insp;
                if (!reachable)
                {
                    var winner = c.FrontmostAt(b.center);
                    hidden.Add($"{insp.name}/{insp.GetType().Name}@{insp.transform.position} collider {col.GetType().Name} trigger={col.isTrigger} bounds {b} " +
                               $"(centre goes to {(winner != null ? winner.name + "/" + winner.GetType().Name + "@" + winner.transform.position : "nothing")})");
                }
            }
            CollectionAssert.IsEmpty(hidden, "never the frontmost tap target: " + string.Join(", ", hidden));
        }

        [UnityTest]
        public IEnumerator HapticsAndReducedMotionTogglesWork()
        {
            yield return Boot();
            var root = GameRoot.Instance;
            root.Settings.Update(s => s.Haptics = false);
            var before = Feel.HapticsPlayed;
            FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
            Assert.AreEqual(before, Feel.HapticsPlayed, "haptics off: none played");
            root.Settings.Update(s => s.Haptics = true);
            FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
            Assert.AreEqual(before + 1, Feel.HapticsPlayed);
            FeedbackAudio.Play(FeedbackAudio.Cue.Discovery);
            Assert.AreEqual(FeedbackAudio.Cue.Discovery, FeedbackAudio.LastCue);

            root.Settings.Update(s => s.ReducedMotion = true);
            Assert.IsTrue(Feel.ReducedMotion);
            var motor = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            for (var i = 0; i < 30; i++) { motor.PlaySwing(); yield return null; }
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, motor.transform.Find("Visual").localEulerAngles.z), 0.01f, "no work swing in reduced motion");
        }

        [UnityTest]
        public IEnumerator FirstTipShowsAndAdvances()
        {
            yield return Boot();
            var hud = GameRoot.Instance.Hud;
            yield return new WaitForSeconds(0.5f);
            StringAssert.Contains("Drag anywhere", hud.Tips.CurrentText);
            yield return Capture.Take("phase8_first_tip");
            hud.ShowJobBoard();
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual("", hud.Tips.CurrentText, "tips hide while a panel is open");
            hud.JobBoard.Close();
            hud.Tips.Acknowledge();
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual("tutorial.deliver", hud.Tips.CurrentStepId);
            hud.Tips.HideAll();
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual("", hud.Tips.CurrentText);
        }
    }
}
