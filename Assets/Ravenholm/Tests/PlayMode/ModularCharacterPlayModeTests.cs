using System;
using System.Collections;
using System.Collections.Generic;
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
    /// <summary>
    /// Astra's modular characters (2026-10-18): the citizen drawn as a head over a worn outfit on one canvas. In all eight
    /// directions, idle, walking, turning, carrying, and with the steel coat put on and taken off mid-walk: the head rides on
    /// the torso at zero offset, the outfit swap changes nothing but the torso (pose, phase, feet and head stay where an
    /// unswapped twin has them), and a second skin palette reaches the head, neck and hands alike.
    /// </summary>
    public sealed class ModularCharacterPlayModeTests
    {
        private static readonly string[] Dirs = { "S", "SE", "E", "NE", "N", "NW", "W", "SW" };
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-modular", Guid.NewGuid().ToString("N"));
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
            for (var i = 0; i < 3; i++) yield return null;
        }

        private static HoseRig Figure(Transform stage, ApplicantCrowdView crowd, string name, Vector2 at)
        {
            var go = CitizenFigure.Create(stage, name, at, crowd.Citizen, crowd.Animations);
            var animator = go.GetComponentInChildren<SupplierAnimator>();
            animator.enabled = false;   // driven by hand here
            return go.GetComponentInChildren<HoseRig>();
        }

        private static SpriteRenderer Part(HoseRig rig, string name) =>
            rig.GetComponentsInChildren<SpriteRenderer>(true).First(r => r.name == name);

        [UnityTest]
        public IEnumerator EightDirectionsWalkEquipCarryAndSkin()
        {
            yield return Boot();
            var crowd = Object.FindAnyObjectByType<ApplicantCrowdView>();
            Assert.IsNotNull(crowd, "the applicants' crowd (the citizen figure's home)");
            var def = crowd.Citizen;
            Assert.IsTrue(def.Modular, "the citizen is modular: a head per view and outfits");
            Assert.AreEqual("citizen_blue", def.defaultOutfit);
            Assert.AreEqual("steel_armour", def.OutfitForArmour("item.steel_coat"), "the steel coat is worn as the steel outfit");
            Assert.IsNull(def.OutfitForArmour("item.riveted_coat"), "other armour: the everyday outfit");

            var stage = new GameObject("ModularStage").transform;
            stage.position = new Vector3(400f, 400f, 0f);   // off the level, near enough for float precision
            var a = Figure(stage, crowd, "Swapper", stage.position);
            var b = Figure(stage, crowd, "Twin", (Vector2)stage.position + new Vector2(30f, 0f));
            var offset = b.transform.position - a.transform.position;
            yield return null;

            // Eight directions, idle and walking, each outfit; the swap mid-walk against an unswapped twin.
            foreach (var dir in Dirs)
            foreach (var pose in new[] { HoseRig.Pose.Idle, HoseRig.Pose.Walk })
            for (var frame = 0; frame < 24; frame++)
            {
                // The swapper puts the coat on at frame 8 and takes it off at frame 16, mid-stride.
                a.ArmourItem = frame >= 8 && frame < 16 ? "item.steel_coat" : null;
                a.Drive(dir, pose, pose == HoseRig.Pose.Walk ? 0.05f : 0f, 1f / 30f, Vector2.zero, 1f);
                b.Drive(dir, pose, pose == HoseRig.Pose.Walk ? 0.05f : 0f, 1f / 30f, Vector2.zero, 1f);
                var view = def.Get(a.ViewName);
                var head = a.HeadAttachment;
                Assert.IsNotNull(head, "a head");
                Assert.AreSame(a.BodyAttachment.transform, head.transform.parent, "the head rides on the torso");
                Assert.AreEqual(Vector3.zero, head.transform.localPosition, "at zero offset (one canvas, one mount)");
                Assert.AreEqual(view.head, head.sprite, $"{dir}: the head of the same view as the torso");
                var outfit = frame >= 8 && frame < 16 ? "steel_armour" : "citizen_blue";
                Assert.AreEqual(outfit, a.WornOutfit);
                Assert.AreEqual(def.TorsoFor(outfit, a.ViewName).torso, a.BodyAttachment.sprite, $"{dir}: the {outfit} torso");
                Assert.AreEqual(dir.EndsWith("W"), a.Mirrored, $"{dir}: mirrored as one rig");
                Assert.Greater(head.sortingOrder, a.BodyAttachment.sortingOrder, "the head over its torso");
                // Nothing but the torso changes: the head, the feet and the hands are where the twin's are.
                Assert.Less(Vector3.Distance(head.transform.position, b.HeadAttachment.transform.position - offset), 1e-3f, $"{dir} {pose} f{frame}: no head jump on the swap");
                Assert.Less(Vector2.Distance(a.LeftFoot, b.LeftFoot), 1e-3f, "no sole-height jump");
                Assert.Less(Vector3.Distance(a.LeftHandAttachment.transform.position, b.LeftHandAttachment.transform.position - offset), 1e-3f, "hands unmoved");
                Assert.AreEqual(a.BodyAttachment.transform.lossyScale, b.BodyAttachment.transform.lossyScale, "no per-outfit scale");
                yield return null;
            }

            // Carrying, in every direction, in steel: drawn, the pack worn, the head still on its torso.
            a.ArmourItem = "item.steel_coat";
            foreach (var dir in Dirs)
            {
                for (var f = 0; f < 6; f++) a.Drive(dir, HoseRig.Pose.Carry, 0.05f, 1f / 30f, Vector2.zero, 1f, carrying: true);
                Assert.AreEqual(Vector3.zero, a.HeadAttachment.transform.localPosition, $"{dir} carrying: the head on its torso");
                Assert.AreEqual(a.HeadAttachment.sprite.bounds.max.y, a.HeadTop, 0.2f, $"{dir}: the head top is the head's (the pile sits under it)");
            }

            // A second skin: the head, the neck (torso) and the hands all take it.
            var skin = new Color32(0x8D, 0x55, 0x24, 255);
            var shadow = new Color32(0x6E, 0x3E, 0x18, 255);
            b.SkinBase = skin;
            b.SkinShadow = shadow;
            b.Drive("SE", HoseRig.Pose.Idle, 0f, 1f / 30f, Vector2.zero, 1f);
            var block = new MaterialPropertyBlock();
            foreach (var part in new[] { b.HeadAttachment, b.BodyAttachment, b.LeftHandAttachment, b.RightHandAttachment })
            {
                Assert.AreSame(def.skinMaterial, part.sharedMaterial, $"{part.name}: skin-tinted");
                part.GetPropertyBlock(block);
                Assert.AreEqual(((Color)skin).r, block.GetColor("_SkinBase").r, 0.02f, $"{part.name}: the second skin");
                Assert.IsNotNull(block.GetTexture("_SkinMask"), $"{part.name}: its mask");
            }

            yield return Sheet(stage, crowd);
            Object.Destroy(stage.gameObject);
        }

        /// <summary>
        /// One picture: every direction in clothes and in steel, walking mid-stride, then carrying with a second skin,
        /// rendered with the game's outline (Captures/modular_characters.png).
        /// </summary>
        private static IEnumerator Sheet(Transform stage, ApplicantCrowdView crowd)
        {
            var rigs = new List<(HoseRig rig, string dir, HoseRig.Pose pose, bool carry)>();
            for (var row = 0; row < 3; row++)
            for (var i = 0; i < Dirs.Length; i++)
            {
                var at = (Vector2)stage.position + new Vector2(100f + i * 1.3f, -row * 1.6f);
                var rig = Figure(stage, crowd, $"Sheet{row}{i}", at);
                rig.ArmourItem = row == 1 ? "item.steel_coat" : null;
                if (row == 2) { rig.SkinBase = new Color32(0x8D, 0x55, 0x24, 255); rig.SkinShadow = new Color32(0x6E, 0x3E, 0x18, 255); }
                rigs.Add((rig, Dirs[i], row == 2 ? HoseRig.Pose.Carry : HoseRig.Pose.Walk, row == 2));
            }
            var cam = new GameObject("SheetCamera").AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 2.9f;
            cam.transform.position = new Vector3(stage.position.x + 100f + 3.5f * 1.3f, stage.position.y - 1.0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0xEF, 0xE7, 0xD6, 255);
            var rt = new RenderTexture(2400, 1500, 24);
            cam.targetTexture = rt;
            for (var f = 0; f < 10; f++)
            {
                foreach (var r in rigs) r.rig.Drive(r.dir, r.pose, 0.05f, 1f / 30f, Vector2.zero, 1f, r.carry);
                yield return null;
            }
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var dir = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Captures"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "modular_characters.png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(cam.gameObject);
        }
    }
}
