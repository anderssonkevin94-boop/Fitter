using System.Collections.Generic;
using System.IO;
using Ravenholm.Content;
using UnityEditor;

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
                if (created)
                {
                    s.EditorInitialize("tutorial." + key, key.Replace('_', ' '));
                    s.EditorSetupStep(order, text, c);
                    EditorUtility.SetDirty(s);
                }
                steps.Add(s);
            }
            Step("move_and_pick_up", 1, TutorialCondition.CarryingSomething,
                "Drag anywhere low on the screen to walk. Walk over the ore pile by the Mine or the logs by the Forest: you pick things up automatically.");
            Step("deliver", 2, TutorialCondition.DeliveredRawMaterial,
                "Carry ore to the Smelter's marker and logs to the Lumbermill's. They process deliveries into ingots and lumber on their own.");
            Step("site_choice", 3, TutorialCondition.SiteDecisionMade,
                "When a Mine section or Forest clearing is worked out, the workers wait. Open the site: continue with the known yield, or go deeper into the unknown.");
            Step("craft", 4, TutorialCondition.CraftedItem,
                "Open the Forge or Workbench and craft a weapon from the starter stock. Crafting is instant; the item appears on the output pad.");
            Step("stock_store", 5, TutorialCondition.GearInStore,
                "Pick the new gear up from the pad and carry it to the Store's counter. Adventurers can only take gear that is in the Store.");
            Step("post_job", 6, TutorialCondition.JobPosted,
                "Open the Job Board (top right) and post Roadside Patrol. You post the job; contractors decide whether to apply and name their fee.");
            Step("send", 7, TutorialCondition.ContractorSent,
                "When applicants arrive at the Store, meet one, compare fees, pick their gear and read the risk word. Then Send.");
            Step("read_result", 8, TutorialCondition.ResultRead,
                "While they are away, keep the Mine and Forest moving. When they return, read the result on the Job Board.");
            Step("danger", 9, TutorialCondition.Acknowledge,
                "Ordinary jobs can go wrong: injury, failure, even death, and the dead lose their gear. A better risk word, armor and a tonic all help.");
            Step("blueprint", 10, TutorialCondition.NewBlueprintOwned,
                "First successes at new jobs teach blueprints. Briar Track and Old Shrine each teach one.");
            Step("upgrade_and_trade", 11, TutorialCondition.UpgradeBought,
                "Gold buys building upgrades, worker tools and Trader goods. Open a building to see its next level and exactly what it needs.");
            Step("time_away", 12, TutorialCondition.Acknowledge,
                "Leave work and a quest running when you go. Sites and facilities keep working for up to 8 hours, and a summary greets you on return.");
            if (catalogue.Tutorial.Count == 0) { catalogue.EditorSetTutorial(steps); EditorUtility.SetDirty(catalogue); }
            AssetDatabase.SaveAssets();
        }
    }
}
