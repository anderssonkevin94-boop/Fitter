using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>What finishes a first-hour prompt. Each is read from game state or a game event, never from a script.</summary>
    public enum TutorialCondition
    {
        /// <summary>Only the player's "Got it" finishes it (information-only tips).</summary>
        Acknowledge,
        CarryingSomething,
        DeliveredRawMaterial,
        SiteDecisionMade,
        CraftedItem,
        GearInStore,
        JobPosted,
        ContractorSent,
        ResultRead,
        NewBlueprintOwned,
        UpgradeBought,
        TraderUsed,
    }

    /// <summary>
    /// One contextual, dismissible first-hour prompt (GDD 19). Prompts never change odds or force
    /// outcomes; a later prompt finishing also finishes earlier ones, so returning players are not nagged.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Tutorial Step", fileName = "tutorial_")]
    public sealed class TutorialStepDefinition : ContentDefinition
    {
        [Min(0)] [SerializeField] private int order;
        [TextArea(2, 4)] [SerializeField] private string prompt = "";
        [SerializeField] private TutorialCondition condition;

        public override string IdKind => "tutorial";
        public int Order => order;
        public string Prompt => prompt;
        public TutorialCondition Condition => condition;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (string.IsNullOrWhiteSpace(prompt)) errors.Add($"{Describe()}: empty prompt");
            if (prompt.Length > 220) errors.Add($"{Describe()}: prompt too long for the tip card (keep under 220 characters)");
        }

#if UNITY_EDITOR
        public void EditorSetupStep(int newOrder, string text, TutorialCondition c)
        {
            order = newOrder; prompt = text; condition = c;
        }
#endif
    }
}
