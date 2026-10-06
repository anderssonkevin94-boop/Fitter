using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    [Serializable]
    public struct CounterValue
    {
        public CounterTag tag;
        [Min(0)] public int strength;
    }

    /// <summary>Contractor class (GDD 10). Base stats are level 1; each level adds the per-level gain to all three.</summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Adventurer Class Definition", fileName = "class_")]
    public sealed class AdventurerClassDefinition : ContentDefinition
    {
        [Min(0)] [SerializeField] private int might = 5;
        [Min(0)] [SerializeField] private int survival = 5;
        [Min(0)] [SerializeField] private int insight = 5;
        [Min(0)] [SerializeField] private int gainPerLevel = 1;
        [Tooltip("Weapon tags this class can wield (GDD 9). Armor is universal.")]
        [SerializeField] private List<WeaponTag> weaponTags = new List<WeaponTag>();
        [Tooltip("Innate context counters (e.g. Mage arcane +4).")]
        [SerializeField] private List<CounterValue> counters = new List<CounterValue>();
        [Min(1)] [SerializeField] private int baseFee = 8;
        [Tooltip("Rogue: an extra rare reward roll after Success or Great Success (Phase 5).")]
        [SerializeField] private bool bonusRareRoll;
        [Tooltip("Character overlay sprite (class outfit and tool).")]
        [SerializeField] private Sprite overlay;
        [TextArea(1, 2)] [SerializeField] private string role = "";

        public override string IdKind => "class";
        public int Might => might;
        public int Survival => survival;
        public int Insight => insight;
        public int GainPerLevel => gainPerLevel;
        public IReadOnlyList<WeaponTag> WeaponTags => weaponTags;
        public IReadOnlyList<CounterValue> Counters => counters;
        public int BaseFee => baseFee;
        public bool BonusRareRoll => bonusRareRoll;
        public Sprite Overlay => overlay;
        public string Role => role;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (weaponTags.Count == 0) errors.Add($"{Describe()}: needs at least one weapon tag");
            if (overlay == null) errors.Add($"{Describe()}: overlay sprite missing");
        }

#if UNITY_EDITOR
        public void EditorSetupClass(int m, int s, int i, List<WeaponTag> tags, List<CounterValue> c, int fee, bool rare, Sprite o, string r)
        {
            might = m; survival = s; insight = i; weaponTags = tags; counters = c; baseFee = fee; bonusRareRoll = rare; overlay = o; role = r;
        }
#endif
    }
}
