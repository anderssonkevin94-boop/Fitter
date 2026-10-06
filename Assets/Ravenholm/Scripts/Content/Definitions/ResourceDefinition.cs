using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    public enum ResourceCategory
    {
        RawMetal,
        RawTimber,
        Herb,
        Processed,
        Rare,
        External,
    }

    /// <summary>Where a carried unit of this resource first enters shared inventory (GDD 8).</summary>
    public enum ReceiverType
    {
        None,
        Smelter,
        Lumbermill,
        Apothecary,
        Store,
    }

    [CreateAssetMenu(menuName = "Ravenholm/Content/Resource Definition", fileName = "resource_")]
    public sealed class ResourceDefinition : ContentDefinition
    {
        /// <summary>Every carried resource unit occupies one carry slot (GDD 3, locked).</summary>
        public const int CarryUnitSize = 1;

        [SerializeField] private ResourceCategory category;

        [Tooltip("Receiver that accepts this resource when carried. Processed and external materials enter shared inventory directly and use None.")]
        [SerializeField] private ReceiverType acceptedReceiver;

        [Tooltip("UI icon (96 px placeholder).")]
        [SerializeField] private Sprite icon;

        [Tooltip("World piece used in carry stacks and piles.")]
        [SerializeField] private Sprite stackSprite;

        public override string IdKind => "resource";
        public ResourceCategory Category => category;
        public ReceiverType AcceptedReceiver => acceptedReceiver;
        public Sprite Icon => icon;
        public Sprite StackSprite => stackSprite;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (icon == null) errors.Add($"{Describe()}: icon is missing");
            if (stackSprite == null) errors.Add($"{Describe()}: stack sprite is missing");
            var raw = category == ResourceCategory.RawMetal || category == ResourceCategory.RawTimber || category == ResourceCategory.Herb;
            if (raw && acceptedReceiver == ReceiverType.None) errors.Add($"{Describe()}: raw resources need a receiver");
        }

#if UNITY_EDITOR
        public void EditorSetup(ResourceCategory newCategory, ReceiverType receiver, Sprite newIcon, Sprite newStack)
        {
            category = newCategory;
            acceptedReceiver = receiver;
            icon = newIcon;
            stackSprite = newStack;
        }
#endif
    }
}
