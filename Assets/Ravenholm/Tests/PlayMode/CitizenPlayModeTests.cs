using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The blond citizen (Astra, 2026-10-15): every NPC for now, drawn and walking exactly like the supplier. Eight
    /// directions (three mirrored), standing and mid-stride, and the adventurers who come to the shop.
    /// </summary>
    public sealed class CitizenPlayModeTests
    {
        private const string CitizenPath = "Assets/Ravenholm/Content/Config/Citizens/citizen_blond_blue.asset";
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-citizen", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameRoot.Instance != null) Object.Destroy(GameRoot.Instance.gameObject);
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
        }

        [UnityTest]
        public IEnumerator TheCitizenWalksLikeTheSupplierInEveryDirection()
        {
            yield return Boot();
            var citizen = AssetDatabase.LoadAssetAtPath<HoseCharacterDefinition>(CitizenPath);
            var owner = AssetDatabase.LoadAssetAtPath<HoseCharacterDefinition>("Assets/Ravenholm/Content/Config/StoreOwner.asset");
            var anims = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>("Assets/Ravenholm/Content/Config/SupplierAnimations.asset");
            Assert.NotNull(citizen, "setup builds the citizen");
            Assert.AreEqual(owner.walkSpeed, citizen.walkSpeed, "the supplier's walk");
            Assert.AreEqual(owner.cadence, citizen.cadence);
            Assert.IsFalse(citizen.sleeves, "sleeves are painted on his body");
            Assert.AreEqual(owner.handOpenFront, citizen.handOpenFront, "shared hands");
            Assert.AreEqual(owner.Get("S").bootLeft, citizen.Get("S").bootLeft, "shared boots");

            // Eight figures around a point, far from town, each walking outward along its direction.
            var cam = SandboxSceneController.Current.WorldCamera;
            var follow = cam.GetComponent<FollowCamera>();
            if (follow != null) follow.enabled = false;
            var at = new Vector2(500f, 500f);
            cam.transform.position = new Vector3(at.x, at.y, cam.transform.position.z);
            cam.orthographicSize = 3.2f;
            var dirs = new[] { ("E", new Vector2(1f, 0f)), ("NE", new Vector2(1f, 0.5f)), ("N", new Vector2(0f, 1f)), ("NW", new Vector2(-1f, 0.5f)),
                               ("W", new Vector2(-1f, 0f)), ("SW", new Vector2(-1f, -0.5f)), ("S", new Vector2(0f, -1f)), ("SE", new Vector2(1f, -0.5f)) };
            var figs = new List<(string dir, Vector2 v, GameObject go, Vector2 start)>();
            for (var i = 0; i < dirs.Length; i++)
            {
                var start = at + new Vector2(i % 2 == 0 ? -0.7f : 0.7f, 1.7f - (i / 2) * 1.5f);   // a 2 x 4 grid that fits a phone frame
                figs.Add((dirs[i].Item1, dirs[i].Item2.normalized, CitizenFigure.Create(null, "Citizen " + dirs[i].Item1, start, citizen, anims), start));
            }
            yield return null;
            // Each walks a short way along its heading at the supplier's walking speed (projected: y halved on screen).
            for (var t = 0f; t < 0.4f; t += Time.deltaTime)
            {
                foreach (var f in figs) f.go.transform.position = f.start + f.v * (t * citizen.walkSpeed * 0.6f);
                yield return null;
            }
            foreach (var f in figs)
            {
                var rig = f.go.GetComponentInChildren<HoseRig>();
                var anim = f.go.GetComponentInChildren<SupplierAnimator>();
                Assert.AreEqual(f.dir, anim.Dir, $"{f.go.name} faces its heading");
                Assert.AreEqual(f.dir.Contains("W"), rig.Mirrored, $"{f.go.name}: W views are mirrored");
                Assert.AreEqual(HoseRig.Pose.Walk, rig.Current, $"{f.go.name} walks");
                Assert.Greater(rig.GroundSpeed, 0f);
            }
            yield return Capture.Take("citizen_directions_walking");
            for (var i = 0; i < 20; i++) yield return null;   // settle to standing
            yield return Capture.Take("citizen_directions_standing");
            foreach (var f in figs) Object.Destroy(f.go);
        }

        [UnityTest]
        public IEnumerator AdventurersAreCitizens()
        {
            yield return Boot();
            var s = GameRoot.Instance.Services;
            foreach (var id in new[] { "item.iron_sword", "item.riveted_coat" }) s.Logistics.System.CreateStoreItem(id, "test stock");
            s.Simulation.Advance(s.Jobs.Board.NextCastleOfferDelay(), 0);
            var p = s.Jobs.State.Posting;
            Assert.NotNull(p);
            s.Simulation.Advance((p.Applicants[0].ArriveMs - p.CreatedMs) / 2, 0);
            yield return null;
            yield return null;
            var crowd = Object.FindAnyObjectByType<ApplicantCrowdView>();
            Assert.AreEqual(1, crowd.ComingCount);
            var walker = crowd.GetComponentsInChildren<HoseRig>();
            Assert.AreEqual(1, walker.Length, "the adventurer on the road is a citizen");
            Assert.AreEqual("citizen_blond_blue", walker[0].Definition.name);
            var player = SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
            player.Teleport((Vector2)walker[0].transform.position + new Vector2(0.8f, 0.3f));
            yield return new WaitForSeconds(0.4f);
            yield return Capture.Take("citizen_adventurer_on_the_road");
            s.Simulation.Advance(p.Applicants[0].ArriveMs - s.Jobs.State.SimMs + 1, 0);
            yield return null;
            yield return null;
            Assert.AreEqual(1, crowd.ShownCount);
            Assert.NotNull(crowd.GetComponentInChildren<ApplicantInspectable>(), "and can be met at the shop");
            player.Teleport(new Vector2(0.6f, 1.2f));
            yield return new WaitForSeconds(0.4f);
            yield return Capture.Take("citizen_adventurer_at_the_shop");
        }
    }
}
