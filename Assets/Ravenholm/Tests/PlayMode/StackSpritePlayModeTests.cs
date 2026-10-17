using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ravenholm.Domain.Logistics;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Astra's carried-item sprites in the game: material, crafted and mixed piles with per-item spacing (each piece on
    /// top of the one below, as high as that piece's step), the drop-off enlargement, delivery arcs and stocked racks.
    /// </summary>
    public sealed class StackSpritePlayModeTests
    {
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-stacks", Guid.NewGuid().ToString("N"));
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

        private static IEnumerator Until(Func<bool> done, float seconds, Func<string> what)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(done(), what());
        }

        private static LogisticsSystem Sys => GameRoot.Instance.Services.Logistics.System;

        /// <summary>Crafted items come off the forge's output pad into the carry.</summary>
        private static void CarryItems(params string[] itemIds)
        {
            const string pad = "container.forge_pad";
            var at = Sys.State.Containers[pad];
            foreach (var id in itemIds) Sys.CreateItemInContainer(pad, id);
            for (var i = 0; i < itemIds.Length; i++) Sys.StepOnce(at.X, at.Y);
        }

        private static void CarryMaterials(params string[] resourceIds)
        {
            foreach (var id in resourceIds)
            {
                Sys.RecordCreated(id);   // as when mined or chopped: the unit exists before it is carried
                Assert.IsTrue(Sys.TryCarry(id), id);
            }
        }

        /// <summary>Each piece sits on top of the one below, as high as that lower piece's step.</summary>
        private static void AssertSpacing(Transform stack, string what)
        {
            var carry = Sys.State.Carry;
            var first = Mathf.Max(0, carry.Count - CarryStackView.MaxVisiblePieces);
            var prevY = 0f;
            var prevStep = 0f;
            for (var i = 0; i < Mathf.Min(carry.Count, CarryStackView.MaxVisiblePieces); i++)
            {
                var piece = stack.Find($"Piece{i}");
                Assert.IsTrue(piece.gameObject.activeSelf, $"{what}: piece {i} shown");
                Assert.NotNull(piece.GetComponent<SpriteRenderer>().sprite, $"{what}: piece {i} has art");
                var step = UnitVisuals.StackStep(carry[first + i]);
                if (i > 0) Assert.AreEqual(prevY + prevStep, piece.localPosition.y, 0.002f, $"{what}: piece {i} rests on piece {i - 1}");
                prevY = piece.localPosition.y;
                prevStep = step;
                Assert.AreEqual(i % 2 == 0 ? -0.02f : 0.02f, piece.localPosition.x, 0.0001f, $"{what}: alternating offsets");
            }
        }

        [UnityTest]
        public IEnumerator PilesRacksAndDeliveriesUseTheNewArt()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            yield return Until(() => GameRoot.Instance != null && SandboxSceneController.Current != null, 10f, () => "town loaded");
            for (var i = 0; i < 3; i++) yield return null;
            var player = SandboxSceneController.Current.Player;
            var motor = player.GetComponent<PlayerMotor>();
            var view = player.GetComponent<CarryStackView>();
            var stack = player.Find("CarryStack");
            Assert.NotNull(stack, "the carried pile");
            yield return new WaitForSeconds(0.3f);   // where he starts: open ground by the plaza

            // A material pile: thin planks stack close (the old fixed step left gaps between them).
            CarryMaterials("resource.cedar_lumber", "resource.cedar_lumber", "resource.maple_lumber", "resource.maple_lumber",
                "resource.ironwood_lumber", "resource.ironwood_lumber", "resource.cedar_lumber", "resource.maple_lumber");
            yield return new WaitForSeconds(0.3f);
            AssertSpacing(stack, "planks");
            Assert.Less(view.PileTop, 8 * 0.13f * 0.75f, "eight planks are much lower than eight 0.13 steps");
            yield return Capture.Take("stack_pile_materials");
            Sys.DebugUnloadCarry();

            // A crafted-item pile: blades, bows, armour, potions.
            CarryItems("item.iron_sword", "item.maple_longbow", "item.steel_coat", "item.antidote",
                "item.moonsilver_blade", "item.cedar_staff", "item.fur_brigandine", "item.warding_draught");
            Assert.AreEqual(8, Sys.CarryCount, "eight crafted pieces carried");
            yield return new WaitForSeconds(0.3f);
            AssertSpacing(stack, "crafted");
            yield return Capture.Take("stack_pile_crafted");
            Sys.DebugUnloadCarry();

            // A mixed pile, more than the eight shown: the badge counts all of them.
            CarryMaterials("resource.iron_ore", "resource.cedar_log", "resource.maple_lumber", "resource.copper_ingot", "resource.gem");
            CarryItems("item.steel_sword", "item.recovery_tonic", "item.ironwood_greatbow", "item.riveted_coat", "item.iron_dagger");
            Assert.AreEqual(10, Sys.CarryCount);
            yield return new WaitForSeconds(0.3f);
            AssertSpacing(stack, "mixed");
            Assert.IsFalse(stack.Find($"Piece{CarryStackView.MaxVisiblePieces - 1}") == null);
            var badge = stack.Find("CountBadge").GetComponent<TMPro.TextMeshPro>();
            StringAssert.StartsWith("10/", badge.text, "the badge counts every unit");
            Assert.Greater(badge.transform.localPosition.y, view.PileTop, "the badge sits above the pile");
            yield return Capture.Take("stack_pile_mixed");
            Sys.DebugUnloadCarry();

            // At the store: the pieces it takes grow 10%; delivering flies each one over in an arc; the racks fill.
            CarryItems("item.iron_sword", "item.steel_maul", "item.maple_longbow", "item.steel_coat", "item.moonsilver_mail",
                "item.antidote", "item.strong_tonic", "item.pathfinder_draught");
            var store = Sys.State.Receivers["receiver.store"];
            motor.Teleport(new Vector2(store.X + 1.0f, store.Y - 0.6f));
            yield return new WaitForSeconds(0.3f);
            var grown = 0;
            for (var i = 0; i < 8; i++)
            {
                // Drawn at its view's scale (Astra's four views: half the single stack sprite's), then 10% larger.
                var baseScale = UnitVisuals.CarryView(Sys.State.Carry[i], view.Facing) != null ? UnitVisuals.CarryViewScale : 1f;
                if (Mathf.Abs(stack.Find($"Piece{i}").localScale.x - 1.1f * baseScale) < 0.01f) grown++;
            }
            Assert.Greater(grown, 0, "pieces the store takes are enlarged 10%");
            var flew = false;
            for (var n = 0; n < 8; n++)
            {
                Sys.StepOnce(store.X, store.Y);
                yield return null;
                foreach (var go in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                    if (go.name == "Flyer" && go.gameObject.activeInHierarchy && go.sprite != null && go.sprite.name.StartsWith("stack_")) flew = true;
            }
            Assert.IsTrue(flew, "delivered pieces fly to the store with their art");
            Assert.AreEqual(0, Sys.CarryCount, "all delivered");
            motor.Teleport(new Vector2(0f, 2.6f));
            yield return new WaitForSeconds(0.6f);
            foreach (var rack in ShopView.Current.Views.Values)
            {
                if (rack.Definition.Display == Ravenholm.Content.FurnitureDisplay.None || rack.Definition.Display == Ravenholm.Content.FurnitureDisplay.Small) continue;
                Assert.Greater(rack.VisibleCount, 0, $"{rack.name} shows stock");
                foreach (var sr in rack.GetComponentsInChildren<SpriteRenderer>())
                    if (sr.gameObject.activeInHierarchy && sr.sprite != null && sr.sprite.name.StartsWith("stack_"))
                        Assert.AreEqual(512f, sr.sprite.pixelsPerUnit, 0.01f, "racks show the new art");
            }
            yield return Capture.Take("stack_racks");
            CollectionAssert.IsEmpty(Sys.Audit());
        }

        /// <summary>
        /// Astra's four item views (Kevin, 2026-10-17: "only one angle is used"): the carried pile turns with the supplier,
        /// each piece crosswise to his way: across the screen facing the camera or away, end-on facing left or right, along
        /// the other diagonal on the diagonals.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCarriedPileTurnsWithTheSupplier()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            yield return Until(() => GameRoot.Instance != null && SandboxSceneController.Current != null, 10f, () => "town loaded");
            for (var i = 0; i < 3; i++) yield return null;
            GameRoot.Instance.Hud.Tips.HideAll();
            var player = SandboxSceneController.Current.Player;
            var view = player.GetComponent<CarryStackView>();
            var stack = player.Find("CarryStack");
            CarryMaterials("resource.cedar_log", "resource.maple_log", "resource.iron_ingot", "resource.cedar_lumber");
            CarryItems("item.iron_sword", "item.steel_coat");
            var router = GameRoot.Instance.InputRouter;
            string[] dirs = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
            for (var d = 0; d < dirs.Length; d++)
            {
                var a = d * Mathf.PI / 4f;
                router.ScriptedMove = new Vector2(Mathf.Cos(a), Mathf.Sin(a));   // the stick: he faces it
                yield return new WaitForSeconds(0.12f);
                router.ScriptedMove = null;
                yield return new WaitForSeconds(0.35f);
                Assert.AreEqual(dirs[d], view.Facing, $"the pile faces {dirs[d]} with him");
                var carry = Sys.State.Carry;
                for (var k = 0; k < carry.Count; k++)
                {
                    var expect = UnitVisuals.CarryView(carry[k], dirs[d]);
                    Assert.NotNull(expect, $"{carry[k].Id}: has views");
                    Assert.AreSame(expect, stack.Find($"Piece{k}").GetComponent<SpriteRenderer>().sprite, $"{dirs[d]}: piece {k} ({carry[k].Id}) shows its {dirs[d]} view");
                }
                yield return Capture.Take($"carry_views_{dirs[d]}");
            }
            var log = Sys.State.Carry[0];
            Assert.AreNotSame(UnitVisuals.CarryView(log, "S"), UnitVisuals.CarryView(log, "E"), "a log across the screen facing the camera, end-on facing right");
            Sys.DebugUnloadCarry();
        }
    }
}