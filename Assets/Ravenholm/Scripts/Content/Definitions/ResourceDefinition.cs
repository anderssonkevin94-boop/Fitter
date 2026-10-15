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
        /// <summary>Foraged food (berries, mushrooms): cooked at the Kitchen.</summary>
        Food,
    }

    /// <summary>Where a carried unit of this resource first enters shared inventory (GDD 8).</summary>
    public enum ReceiverType
    {
        None,
        Smelter,
        Lumbermill,
        Apothecary,
        Store,
        Kitchen,
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
        [Tooltip("Height between this piece and the one above it in a carried pile (local units before the pile's scale). Thin pieces (planks, blades, bows) stack closer. From the art's suggested-spacing.json.")]
        [Min(0.01f)] [SerializeField] private float stackStep = 0.13f;

        [Tooltip("Mine ore: the painted block shown where this ore sits in the rock, within the lamp's light (optional).")]
        [SerializeField] private Sprite mineBlock;

        [Tooltip("Colour used when this resource appears in site diagrams (strata, trail).")]
        [SerializeField] private Color mapTint = new Color(0.6f, 0.6f, 0.6f, 1f);

        public override string IdKind => "resource";
        public ResourceCategory Category => category;
        public ReceiverType AcceptedReceiver => acceptedReceiver;
        public Sprite Icon => icon;
        public Sprite StackSprite => stackSprite;
        public float StackStep => stackStep;
        public Color MapTint => mapTint;
        public Sprite MineBlock => mineBlock;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (icon == null) errors.Add($"{Describe()}: icon is missing");
            if (stackSprite == null) errors.Add($"{Describe()}: stack sprite is missing");
            var raw = category == ResourceCategory.RawMetal || category == ResourceCategory.RawTimber || category == ResourceCategory.Herb;
            if (raw && acceptedReceiver == ReceiverType.None) errors.Add($"{Describe()}: raw resources need a receiver");
        }

#if UNITY_EDITOR
        public void EditorSetTint(Color c) => mapTint = c;

        public void EditorSetup(ResourceCategory newCategory, ReceiverType receiver, Sprite newIcon, Sprite newStack)
        {
            category = newCategory;
            acceptedReceiver = receiver;
            icon = newIcon;
            stackSprite = newStack;
        }
        public void EditorSetStackStep(float step) => stackStep = step;
#endif
    }
}
