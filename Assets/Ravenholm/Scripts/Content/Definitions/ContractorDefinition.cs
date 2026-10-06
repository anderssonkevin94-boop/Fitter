using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>
    /// A fixed contractor fixture (GDD 11 examples, or the sponsored recovery contractor).
    /// Other contractors are generated from the name pool. Fixtures are not permanent promises.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Contractor Definition", fileName = "contractor_")]
    public sealed class ContractorDefinition : ContentDefinition
    {
        [SerializeField] private AdventurerClassDefinition adventurerClass;
        [Min(1)] [SerializeField] private int level = 1;
        [SerializeField] private List<TraitDefinition> traits = new List<TraitDefinition>();
        [Tooltip("Saved per contractor; 0.9 to 1.15 (GDD 10).")]
        [Range(0.9f, 1.15f)] [SerializeField] private float personalityPrice = 1f;
        [Tooltip("Applies only to the recovery contract; the patron pays the fee.")]
        [SerializeField] private bool sponsored;
        [Range(0, 2)] [SerializeField] private int skin;

        public override string IdKind => "contractor";
        public AdventurerClassDefinition Class => adventurerClass;
        public int Level => level;
        public IReadOnlyList<TraitDefinition> Traits => traits;
        public float PersonalityPrice => personalityPrice;
        public bool Sponsored => sponsored;
        public int Skin => skin;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (adventurerClass == null) errors.Add($"{Describe()}: class missing");
            if (traits.Count > 2) errors.Add($"{Describe()}: at most two traits");
        }

#if UNITY_EDITOR
        public void EditorSetupContractor(AdventurerClassDefinition c, int lvl, List<TraitDefinition> t, float price, bool sponsor, int skinIndex)
        {
            adventurerClass = c; level = lvl; traits = t; personalityPrice = price; sponsored = sponsor; skin = skinIndex;
        }
#endif
    }
}
