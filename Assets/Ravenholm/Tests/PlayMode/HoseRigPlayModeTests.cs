using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The rubber-hose supplier: every direction and pose renders (a sheet in Captures/), feet stay planted while
    /// walking, the supplier in town uses the rig, and the carried pile rests on his hands.
    /// </summary>
    public sealed class HoseRigPlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-hose", Guid.NewGuid().ToString("N"));
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

        private static IEnumerator Boot()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((GameRoot.Instance == null || SandboxSceneController.Current == null) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.NotNull(SandboxSceneController.Current, "town loaded");
            for (var i = 0; i < 3; i++) yield return null;
        }

        private static readonly List<Canvas> HiddenCanvases = new List<Canvas>();

        private static void HideHud(bool hide)
        {
            if (hide)
            {
                HiddenCanvases.Clear();
                foreach (var c in Object.FindObjectsByType<Canvas>())
                    if (c.enabled) { c.enabled = false; HiddenCanvases.Add(c); }
            }
            else foreach (var c in HiddenCanvases) if (c != null) c.enabled = true;
        }

        private static Texture2D Grab(Camera cam, RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        /// <summary>
        /// A walk cycle to look at: the five painted directions walking across a still camera at walking speed (so
        /// planted feet show as boots that don't move), frames saved for a GIF.
        /// </summary>
        [UnityTest]
        public IEnumerator WalkCycleFrames()
        {
            yield return Boot();
            var def = SandboxSceneController.Current.Player.GetComponentInChildren<HoseRig>().Definition;
            HideHud(true);
            var origin = new Vector2(3000f, 3000f);
            var cam = SandboxSceneController.Current.WorldCamera;
            var follow = cam.GetComponent<FollowCamera>();
            if (follow != null) follow.enabled = false;
            cam.transform.position = new Vector3(origin.x + 1.6f, origin.y - 1.95f, cam.transform.position.z);
            cam.orthographicSize = 3.45f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.86f, 0.8f, 0.66f, 1f);
            // Walking speed as in game: 2 units/s sideways, 1 up or down (the camera looks down at the ground).
            var walkers = new List<(HoseRig rig, string dir, Vector2 vel)>
            {
                (MakeRig(def, origin + new Vector2(0f, 0f)), "E", new Vector2(2f, 0f)),
                (MakeRig(def, origin + new Vector2(0f, -1.3f)), "SE", new Vector2(1.414f, -0.707f)),
                (MakeRig(def, origin + new Vector2(1.6f, -2.6f)), "S", new Vector2(0f, -1f)),
                (MakeRig(def, origin + new Vector2(0f, -4.6f)), "NE", new Vector2(1.414f, 0.707f)),
                (MakeRig(def, origin + new Vector2(2.4f, -5.0f)), "N", new Vector2(0f, 1f)),
            };
            var rt = new RenderTexture(900, 1500, 24);
            cam.targetTexture = rt;
            var dir = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Captures", "hose_walk_frames"));
            Directory.CreateDirectory(dir);
            const float dt = 1f / 30f;
            for (var f = 0; f < 45; f++)
            {
                foreach (var (rig, d, vel) in walkers)
                {
                    var root = rig.transform.parent;
                    root.position += (Vector3)(vel * dt);
                    rig.Drive(d, HoseRig.Pose.Walk, vel.magnitude * dt, dt, Vector2.zero, 1f);
                }
                yield return null;
                yield return null;
                var tex = Grab(cam, rt);
                File.WriteAllBytes(Path.Combine(dir, $"f{f:D2}.png"), tex.EncodeToPNG());
                Object.Destroy(tex);
            }
            cam.targetTexture = null;
            rt.Release();
            HideHud(false);
            if (follow != null) follow.enabled = true;
        }

        /// <summary>One mining swing (and one chop) in five views, eight moments each: a sheet to check the swing reads.</summary>
        [UnityTest]
        public IEnumerator SwingFrames()
        {
            yield return Boot();
            var def = SandboxSceneController.Current.Player.GetComponentInChildren<HoseRig>().Definition;
            string[] views = { "E", "SE", "S", "NE", "N" };
            var origin = new Vector2(4000f, 4000f);
            var rigs = new List<(HoseRig rig, float t, HoseRig.Pose pose)>();
            float[] moments = { 0.04f, 0.1f, 0.2f, 0.225f, 0.25f, 0.28f, 0.36f, 0.44f };   // wind-up .. strike .. hold .. recover
            for (var r = 0; r < views.Length + 1; r++)
            for (var c = 0; c < moments.Length; c++)
                rigs.Add((MakeRig(def, origin + new Vector2(c * 1.0f, -r * 1.35f)), moments[c], r < views.Length ? HoseRig.Pose.Mine : HoseRig.Pose.Chop));
            var raisedBehind = false;
            for (var i = 0; i < rigs.Count; i++)
            {
                var (rig, t, pose) = rigs[i];
                var view = i / moments.Length < views.Length ? views[i / moments.Length] : "E";
                var steps = Mathf.RoundToInt(t / 0.01f);
                for (var k = 0; k < steps; k++) rig.Drive(view, pose, 0f, 0.01f, Vector2.zero, 1f);
                if (rig.SwingAngle < -20f) raisedBehind = true;
            }
            Assert.IsTrue(raisedBehind, "the wind-up raises the tool behind his head");
            yield return null;
            yield return Shoot("hose_swing_sheet", origin + new Vector2(3.5f, -3.3f), 4.4f, 1600, 1760);
            foreach (var (rig, _, _) in rigs) Object.Destroy(rig.transform.parent.gameObject);
        }

        private static IEnumerator Shoot(string name, Vector2 centre, float ortho, int w, int h)
        {
            HideHud(true);
            var cam = SandboxSceneController.Current.WorldCamera;
            var follow = cam.GetComponent<FollowCamera>();
            if (follow != null) follow.enabled = false;
            cam.transform.position = new Vector3(centre.x, centre.y, cam.transform.position.z);
            cam.orthographicSize = ortho;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.86f, 0.8f, 0.66f, 1f);
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            for (var i = 0; i < 3; i++) yield return null;
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
            if (follow != null) follow.enabled = true;
            HideHud(false);
        }

        private static HoseRig MakeRig(HoseCharacterDefinition def, Vector2 at)
        {
            var root = new GameObject("HoseTest");
            root.transform.position = at;
            root.AddComponent<SpriteOutline>();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var rig = visual.AddComponent<HoseRig>();
            rig.EditorSetup(def);
            return rig;
        }

        [UnityTest]
        public IEnumerator EveryDirectionAndPoseRenders()
        {
            yield return Boot();
            var player = SandboxSceneController.Current.Player;
            var townRig = player.GetComponentInChildren<HoseRig>();
            Assert.NotNull(townRig, "the supplier is the rubber-hose character");
            var def = townRig.Definition;
            Assert.AreEqual(5, def.views.Length);
            foreach (var v in def.views) Assert.NotNull(v.body, v.name);

            // A sheet far from town: rows = idle, walk (mid-stride), carry, mine, chop; columns = the eight directions.
            string[] dirs = { "S", "SE", "E", "NE", "N", "NW", "W", "SW" };
            var poses = new[] { HoseRig.Pose.Idle, HoseRig.Pose.Walk, HoseRig.Pose.Carry, HoseRig.Pose.Mine, HoseRig.Pose.Chop };
            var origin = new Vector2(2000f, 2000f);
            var rigs = new List<(HoseRig rig, string dir, HoseRig.Pose pose)>();
            for (var r = 0; r < poses.Length; r++)
            for (var c = 0; c < dirs.Length; c++)
                rigs.Add((MakeRig(def, origin + new Vector2(c * 1.0f, -r * 1.4f)), dirs[c], poses[r]));
            // Drive them for a moment: walkers cover ground at walking pace; workers swing into the strike.
            for (var f = 0; f < 20; f++)
            {
                foreach (var (rig, dir, pose) in rigs)
                {
                    var moving = pose == HoseRig.Pose.Walk || pose == HoseRig.Pose.Carry;
                    rig.Drive(dir, pose, moving ? 0.03f : 0f, 1f / 60f, Vector2.zero, 1f);
                }
                yield return null;
            }
            foreach (var (rig, _, pose) in rigs)
            {
                if (pose == HoseRig.Pose.Carry) Assert.IsTrue(rig.CarryAnchor.HasValue, "carrying holds the hands out for the pile");
                if (pose == HoseRig.Pose.Mine || pose == HoseRig.Pose.Chop) Assert.AreNotEqual(0f, rig.ToolAngle, "the tool swings");
            }
            yield return Shoot("hose_sheet_ingame", origin + new Vector2(3.5f, -2.8f), 3.6f, 1600, 1440);
            foreach (var (rig, _, _) in rigs) Object.Destroy(rig.transform.parent.gameObject);
        }

        [UnityTest]
        public IEnumerator FeetStayPlantedWhileWalking()
        {
            yield return Boot();
            var player = SandboxSceneController.Current.Player;
            var anim = player.GetComponentInChildren<SupplierAnimator>();
            var rig = anim.Rig;
            Assert.NotNull(rig);
            GameRoot.Instance.InputRouter.ScriptedMove = Vector2.right;
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(HoseRig.Pose.Walk, rig.Current, "walking");
            // While the left foot supports him (the first 60% of the phase), it doesn't move on the ground.
            var samples = 0;
            var worst = 0f;
            Vector2? planted = null;
            for (var i = 0; i < 90 && samples < 6; i++)
            {
                yield return null;
                var inStance = rig.Phase > 0.05f && rig.Phase < 0.55f;
                var world = (Vector2)rig.transform.Find("HoseRig").TransformPoint(rig.LeftFoot);
                if (!inStance) { planted = null; continue; }
                if (planted == null) { planted = world; continue; }
                worst = Mathf.Max(worst, Mathf.Abs(world.x - planted.Value.x));
                samples++;
            }
            Assert.Greater(samples, 3, "sampled a stance");
            Assert.Less(worst, 0.02f, "the supporting foot stays put while the body walks over it");
            yield return Capture.Take("hose_walk_town");
            GameRoot.Instance.InputRouter.ScriptedMove = null;

            // Carrying: the pile rests on his hands, out in front.
            var sys = GameRoot.Instance.Services.Logistics.System;
            sys.RecordCreated("resource.iron_ore");
            Assert.IsTrue(sys.TryCarry("resource.iron_ore"));
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(anim.CarryAnchor.HasValue, "the hands hold the pile");
            var stack = player.Find("CarryStack");
            Assert.Less(stack.localPosition.y, anim.HeadTop - 0.2f, "at hand height, not on the hat");
            yield return Capture.Take("hose_carry_town");
        }
    }
}
