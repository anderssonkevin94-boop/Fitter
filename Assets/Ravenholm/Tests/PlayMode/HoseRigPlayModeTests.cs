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
            // The spec's walking speed on the ground, projected (up or down the screen is half the screen distance).
            var v = def.walkSpeed;
            var walkers = new List<(HoseRig rig, string dir, Vector2 vel)>
            {
                (MakeRig(def, origin + new Vector2(0f, 0f)), "E", new Vector2(v, 0f)),
                (MakeRig(def, origin + new Vector2(0f, -1.3f)), "SE", new Vector2(0.707f, -0.354f) * v),
                (MakeRig(def, origin + new Vector2(1.6f, -2.6f)), "S", new Vector2(0f, -0.5f) * v),
                (MakeRig(def, origin + new Vector2(0f, -4.6f)), "NE", new Vector2(0.707f, 0.354f) * v),
                (MakeRig(def, origin + new Vector2(2.4f, -5.0f)), "N", new Vector2(0f, 0.5f) * v),
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
            // Five views mining, then the same five chopping (Kevin's playtest, 2026-10-17: the swing clips the body).
            for (var r = 0; r < views.Length * 2; r++)
            for (var c = 0; c < moments.Length; c++)
                rigs.Add((MakeRig(def, origin + new Vector2(c * 1.0f, -r * 1.35f)), moments[c], r < views.Length ? HoseRig.Pose.Mine : HoseRig.Pose.Chop));
            var raisedBehind = false;
            for (var i = 0; i < rigs.Count; i++)
            {
                var (rig, t, pose) = rigs[i];
                var view = views[(i / moments.Length) % views.Length];
                var steps = Mathf.RoundToInt(t / 0.01f);
                for (var k = 0; k < steps; k++) rig.Drive(view, pose, 0f, 0.01f, Vector2.zero, 1f);
                if (rig.SwingAngle < -20f) raisedBehind = true;
            }
            Assert.IsTrue(raisedBehind, "the wind-up raises the tool behind his head");
            yield return null;
            yield return Shoot("hose_swing_sheet", origin + new Vector2(3.5f, -6.0f), 7.1f, 1400, 2480);
            foreach (var (rig, _, _) in rigs) Object.Destroy(rig.transform.parent.gameObject);
        }

        /// <summary>The spec's review sheet: all eight directions at four moments of a step (contact, down, passing, up).</summary>
        [UnityTest]
        public IEnumerator WalkKeyPoses()
        {
            yield return Boot();
            var def = SandboxSceneController.Current.Player.GetComponentInChildren<HoseRig>().Definition;
            string[] dirs = { "S", "SE", "E", "NE", "N", "NW", "W", "SW" };
            float[] phases = { 0f, 0.1f, 0.25f, 0.35f };
            var origin = new Vector2(5000f, 5000f);
            var rigs = new List<HoseRig>();
            for (var r = 0; r < phases.Length; r++)
            for (var c = 0; c < dirs.Length; c++)
            {
                var rig = MakeRig(def, origin + new Vector2(c * 1.0f, -r * 1.4f));
                rigs.Add(rig);
                // Walk into a steady stride, then on to the wanted moment (small steps, one ground unit = depth on screen).
                var depth = dirs[c] == "S" || dirs[c] == "N" ? 0.5f : dirs[c] == "E" || dirs[c] == "W" ? 1f : 0.79f;
                for (var i = 0; i < 60; i++) rig.Drive(dirs[c], HoseRig.Pose.Walk, 0.01f * depth, 1f / 60f, Vector2.zero, 1f);
                for (var i = 0; i < 400 && Mathf.Abs(Mathf.DeltaAngle(rig.Phase * 360f, phases[r] * 360f)) > 2f; i++)
                    rig.Drive(dirs[c], HoseRig.Pose.Walk, 0.002f * depth, 1f / 300f, Vector2.zero, 1f);
            }
            yield return null;
            yield return Shoot("walk_key_poses", origin + new Vector2(3.5f, -1.75f), 3.4f, 1600, 1360);
            foreach (var rig in rigs) Object.Destroy(rig.transform.parent.gameObject);
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
            root.AddComponent<SortingGroup>();
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
            // The hand rule (spec section 4): relaxed and head-on, both thumbs toward his centre line (the art's thumb
            // points to screen right, so a hand right of centre is mirrored); from the side, the back of the hand, unmirrored.
            foreach (var (rig, dir, pose) in rigs)
            {
                if (pose != HoseRig.Pose.Idle) continue;
                var hl = rig.transform.Find("HoseRig/HandLeft").GetComponent<SpriteRenderer>();
                var hr = rig.transform.Find("HoseRig/HandRight").GetComponent<SpriteRenderer>();
                if (dir == "S" || dir == "N")
                {
                    Assert.AreEqual(hl.transform.localPosition.x > 0f, hl.flipX, $"{dir}: left thumb toward the centre");
                    Assert.AreEqual(hr.transform.localPosition.x > 0f, hr.flipX, $"{dir}: right thumb toward the centre");
                    Assert.AreEqual(def.handOpenSide, hl.sprite, $"{dir}: thumb edge on");
                }
                else
                {
                    Assert.IsFalse(hl.flipX || hr.flipX, $"{dir}: thumbs the way he faces");
                    Assert.AreEqual(def.handOpenBack, hl.sprite, $"{dir}: the back of the hand");
                }
            }
            foreach (var (rig, _, pose) in rigs)
            {
                if (pose == HoseRig.Pose.Carry) Assert.IsTrue(rig.CarryAnchor.HasValue, "carrying holds the hands out for the pile");
                if (pose == HoseRig.Pose.Mine || pose == HoseRig.Pose.Chop) Assert.AreNotEqual(0f, rig.ToolAngle, "the tool swings");
            }
            yield return Shoot("hose_sheet_ingame", origin + new Vector2(3.5f, -2.4f), 4.0f, 1600, 1600);
            foreach (var (rig, _, _) in rigs) Object.Destroy(rig.transform.parent.gameObject);
        }

        private static Vector2 OpenGround(Vector2 near, float room)
        {
            var mask = LayerMask.GetMask(PlayerMotor.BlockerLayerName);
            var slab = MineSlabView.Current;
            for (var r = 0f; r < 20f; r += 0.5f)
            for (var a = 0; a < 16; a++)
            {
                var c = near + new Vector2(Mathf.Cos(a * Mathf.PI / 8f), Mathf.Sin(a * Mathf.PI / 8f)) * r;
                if (Physics2D.OverlapCircle(c, room, mask) != null) continue;
                var ok = true;
                for (var b = 0; b < 16 && ok; b++)
                {
                    var q = c + new Vector2(Mathf.Cos(b * Mathf.PI / 8f), Mathf.Sin(b * Mathf.PI / 8f) * 0.5f) * room;
                    if (slab != null && !slab.SurfaceWalkable(q)) ok = false;
                }
                if (ok) return c;
                if (r == 0f) break;
            }
            Assert.Fail("no open ground near the camp to walk in");
            return near;
        }

        /// <summary>
        /// The spec (Docs/ANIMATION_SPEC.md, section 3) in all eight directions, walking in town: the ground speed,
        /// the same cadence everywhere, the feet's reach and the legs' stretch.
        /// </summary>
        [UnityTest]
        public IEnumerator TheWalkMeetsTheSpecInEveryDirection()
        {
            yield return Boot();
            var player = SandboxSceneController.Current.Player;
            var motor = player.GetComponent<PlayerMotor>();
            var rig = player.GetComponentInChildren<SupplierAnimator>().Rig;
            var def = rig.Definition;
            // Open ground with room to walk a couple of units every way (since the camps moved to the rim, 2026-10-18,
            // what stands beside the spawn varies with the world; walking into it threw off the cadence).
            var start = OpenGround((Vector2)player.position, 2.6f);
            var cadences = new List<float>();
            for (var k = 0; k < 8; k++)
            {
                motor.Teleport(start);
                yield return new WaitForSeconds(0.25f);
                var a = k * Mathf.PI / 4f;
                GameRoot.Instance.InputRouter.ScriptedMove = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                yield return new WaitForSeconds(0.35f);   // up to speed and into the walk
                var p0 = (Vector2)player.position;
                var t0 = Time.time;
                var lastPhase = rig.Phase;
                float phaseTravel = 0f, reach = 0f, stretch = 0f;
                while (Time.time - t0 < 0.6f)
                {
                    yield return null;
                    phaseTravel += Mathf.Repeat(rig.Phase - lastPhase, 1f);
                    lastPhase = rig.Phase;
                    reach = Mathf.Max(reach, rig.FootReach);
                    stretch = Mathf.Max(stretch, rig.LegStretch);
                }
                var dt = Time.time - t0;
                var screen = ((Vector2)player.position - p0) / dt;
                var ground = new Vector2(screen.x, screen.y * 2f).magnitude;
                var cadence = 2f * phaseTravel / dt;
                cadences.Add(cadence);
                var label = $"direction {k * 45} deg ({rig.ViewName}{(rig.Mirrored ? " mirrored" : "")})";
                Assert.AreEqual(def.walkSpeed, ground, def.walkSpeed * 0.05f, $"{label}: ground speed");
                Assert.AreEqual(def.cadence, cadence, def.cadence * 0.05f, $"{label}: cadence");
                Assert.LessOrEqual(reach, def.maxFootReach + 0.002f, $"{label}: foot reach");
                Assert.LessOrEqual(stretch, def.maxLegStretch + 0.01f, $"{label}: leg stretch");
            }
            GameRoot.Instance.InputRouter.ScriptedMove = null;
            cadences.Sort();
            Assert.Less(cadences[cadences.Count - 1] / cadences[0], 1.05f, "the same cadence in every direction");
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
            // While the left foot supports him (the first half of the phase) it glides far less than he walks: his legs
            // are short, so at full stride the feet glide a little rather than stretch the legs (Kevin's choice).
            var samples = 0;
            float footDrift = 0f, bodyMoved = 0f;
            Vector2? footStart = null, bodyStart = null;
            for (var i = 0; i < 90 && samples < 6; i++)
            {
                yield return null;
                var inStance = rig.Phase > 0.05f && rig.Phase < rig.Definition.duty - 0.05f;
                var foot = (Vector2)rig.transform.Find("HoseRig").TransformPoint(rig.LeftFoot);
                var body = (Vector2)player.position;
                if (!inStance) { footStart = null; continue; }
                if (footStart == null) { footStart = foot; bodyStart = body; continue; }
                footDrift = Mathf.Abs(foot.x - footStart.Value.x);
                bodyMoved = Mathf.Abs(body.x - bodyStart.Value.x);
                samples++;
            }
            Assert.Greater(samples, 3, "sampled a stance");
            Assert.Greater(bodyMoved, 0.05f, "he walked");
            Assert.Less(footDrift, bodyMoved * 0.5f, $"the supporting foot mostly stays put (glided {footDrift:F3} while he walked {bodyMoved:F3})");
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
