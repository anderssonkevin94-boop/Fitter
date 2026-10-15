using System.Collections.Generic;
using System.IO;
using Ravenholm.Content;
using UnityEditor;
using UnityEngine;

namespace Ravenholm.EditorTools
{
    public static partial class ProjectSetup
    {
        public const string TutorialDir = Root + "Content/Tutorial/";
        /// <summary>
        /// Phase 8 first-hour prompts, following the GDD 19 first-hour hypothesis. Each finishes from game state,
        /// a game event or the player's "Got it"; none changes odds or outcomes.
        /// </summary>
        private static void EnsureTutorialContent(ContentCatalogue catalogue)
        {
            var steps = new List<TutorialStepDefinition>();
            void Step(string key, int order, TutorialCondition c, string text)
            {
                var path = TutorialDir + "tutorial_" + key + ".asset";
                var created = !File.Exists(path);
                var s = EnsureAsset<TutorialStepDefinition>(path);
                if (created) s.EditorInitialize("tutorial." + key, key.Replace('_', ' '));
                if (created || s.Prompt != text)   // wording follows design changes (solo play)
                {
                    s.EditorSetupStep(order, text, c);
                    EditorUtility.SetDirty(s);
                }
                steps.Add(s);
            }
            Step("move_and_pick_up", 1, TutorialCondition.CarryingSomething,
                "Drag anywhere low on the screen to walk. In the forest around camp, hold the action button by a tree to chop, or by the rock of a hill to dig. What you gather goes straight into your carry.");
            Step("deliver", 2, TutorialCondition.DeliveredRawMaterial,
                "Carry ore to the Smelter's marker and logs to the Lumbermill's. They process deliveries into ingots and lumber on their own.");
            // Site decisions went with the site panels (2026-10-15): a tip to read instead.
            Step("site_choice", 3, TutorialCondition.Acknowledge,
                "Ore stays hidden in the rock until you break it. The further out from camp you go, the tougher the trees and rock, with new kinds of wood and deeper, richer ore.");
            Step("craft", 4, TutorialCondition.CraftedItem,
                "Open the Forge or Workbench and craft a weapon from the starter stock. Crafting is instant; the item appears on the output pad.");
            Step("stock_store", 5, TutorialCondition.GearInStore,
                "Pick the new gear up from the pad and carry it into your shop, just inside the entrance. You can only lend adventurers gear that is in your shop.");
            // The castle posts the jobs (2026-10-15).
            Step("post_job", 6, TutorialCondition.JobPosted,
                "The castle down the road posts the jobs. Adventurers take them and walk to your shop to be kitted out: the castle's jobs (top right) shows who is coming.");
            Step("send", 7, TutorialCondition.ContractorSent,
                "When the adventurer arrives at your shop, meet them, pick the gear you lend them and read the risk word. Then Send: they pay you a share of the castle's reward on return."); 
            Step("read_result", 8, TutorialCondition.ResultRead,
                "While they are away, keep clearing the forest and hills. When they return, read the result on the castle's jobs board.");
            Step("danger", 9, TutorialCondition.Acknowledge,
                "Ordinary jobs can go wrong: injury, failure, even death, and the dead lose their gear. A better risk word, armor and a tonic all help.");
            Step("blueprint", 10, TutorialCondition.NewBlueprintOwned,
                "First successes at new jobs teach blueprints. Briar Track and Old Shrine each teach one.");
            Step("upgrade_and_trade", 11, TutorialCondition.UpgradeBought,
                "Gold buys building upgrades, your own tools and Trader goods. Open a building to see its next level and exactly what it needs.");
            Step("time_away", 12, TutorialCondition.Acknowledge,
                "While you're away, the facilities keep processing what you delivered (up to 8 hours) and contractors finish their jobs; a summary greets you on return. Only you gather ore and wood.");
            if (catalogue.Tutorial.Count == 0) { catalogue.EditorSetTutorial(steps); EditorUtility.SetDirty(catalogue); }
            AssetDatabase.SaveAssets();
        }
    }
}
