using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>Only weapons, armor and potions are player-crafted adventuring goods (GDD 8, locked).</summary>
    public enum ItemCategory
    {
        Weapon,
        Armor,
        Potion,
    }

    /// <summary>Weapon compatibility tag; classes list the tags they can use (GDD 9).</summary>
    public enum WeaponTag
    {
        None,
        Blade,
        Blunt,
        Bow,
        LightBlade,
        Staff,
    }

    /// <summary>Threat a property helps counter (GDD 12/17). Used by the Phase 5 resolver; displayed from Phase 3.</summary>
    public enum CounterTag
    {
        None,
        Arcane,
        Poison,
        Exploration,
        Traps,
        HeavyBlows,
    }

    public enum PotionEffect
    {
        None,
        SurvivalMargin,
        PoisonCounter,
        ArcaneCounter,
    }

    /// <summary>
    /// Minimal Phase 0 item schema. Counter properties and display prefabs arrive with the
    /// systems that use them (Phase 3 to 5) rather than as unused fields now.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Item Definition", fileName = "item_")]
    public sealed class ItemDefinition : ContentDefinition
    {
        [SerializeField] private ItemCategory category;

        [Tooltip("Weapons only. Primary readiness stat.")]
        [Min(0)] [SerializeField] private int power;

        [Tooltip("Armor only. Primary survival stat.")]
        [Min(0)] [SerializeField] private int protection;

        [Tooltip("Weapons only. Which classes can wield it.")]
        [SerializeField] private WeaponTag weaponTag;

        [Tooltip("Potions only. The single explicit effect.")]
        [SerializeField] private PotionEffect potionEffect;

        [Tooltip("Potions only. Effect strength (e.g. +4 survival margin).")]
        [Min(0)] [SerializeField] private int potionStrength;

        [Tooltip("Contextual property: which threat this item helps counter, if any.")]
        [SerializeField] private CounterTag counterTag;
        [Min(0)] [SerializeField] private int counterStrength;

        [Tooltip("Gold the periodic Trader pays for one unit. Deliberately poor (GDD 15).")]
        [Min(0)] [SerializeField] private int traderBuyValue;

        [SerializeField] private Sprite icon;
        [SerializeField] private Sprite stackSprite;

        public override string IdKind => "item";
        public ItemCategory Category => category;
        public int Power => power;
        public int Protection => protection;
        public WeaponTag WeaponTag => weaponTag;
        public PotionEffect PotionEffect => potionEffect;
        public int PotionStrength => potionStrength;
        public int TraderBuyValue => traderBuyValue;
        public CounterTag CounterTag => counterTag;
        public int CounterStrength => counterStrength;

        /// <summary>Short stat line for cards, e.g. "Power 10 · arcane counter +4".</summary>
        public string StatLine()
        {
            string main;
            switch (category)
            {
                case ItemCategory.Weapon: main = $"Power {power} · {Readable(weaponTag.ToString())} weapon"; break;
                case ItemCategory.Armor: main = $"Protection {protection}"; break;
                default: main = $"{Readable(potionEffect.ToString())} +{potionStrength}"; break;
            }
            return counterTag == CounterTag.None ? main : $"{main} · {Readable(counterTag.ToString()).ToLowerInvariant()} counter +{counterStrength}";
        }

        /// <summary>"LightBlade" → "Light blade".</summary>
        private static string Readable(string pascal)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < pascal.Length; i++)
            {
                var c = pascal[i];
                if (i > 0 && char.IsUpper(c)) sb.Append(' ').Append(char.ToLowerInvariant(c));
                else sb.Append(c);
            }
            return sb.ToString();
        }
        public Sprite Icon => icon;
        public Sprite StackSprite => stackSprite;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (icon == null) errors.Add($"{Describe()}: icon is missing");
            if (stackSprite == null) errors.Add($"{Describe()}: stack sprite is missing");
            switch (category)
            {
                case ItemCategory.Weapon:
                    if (weaponTag == WeaponTag.None) errors.Add($"{Describe()}: weapon needs a weapon tag");
                    if (power <= 0) errors.Add($"{Describe()}: weapon needs Power > 0");
                    if (protection != 0 || potionEffect != PotionEffect.None) errors.Add($"{Describe()}: weapon has armor/potion fields set");
                    break;
                case ItemCategory.Armor:
                    if (protection <= 0) errors.Add($"{Describe()}: armor needs Protection > 0");
                    if (power != 0 || weaponTag != WeaponTag.None || potionEffect != PotionEffect.None) errors.Add($"{Describe()}: armor has weapon/potion fields set");
                    break;
                case ItemCategory.Potion:
                    if (potionEffect == PotionEffect.None || potionStrength <= 0) errors.Add($"{Describe()}: potion needs one effect with strength > 0");
                    if (power != 0 || protection != 0 || weaponTag != WeaponTag.None) errors.Add($"{Describe()}: potion has weapon/armor fields set");
                    break;
            }
        }

#if UNITY_EDITOR
        public void EditorSetCounter(CounterTag tag, int strength)
        {
            counterTag = tag;
            counterStrength = strength;
        }

        public void EditorSetup(ItemCategory newCategory, int newPower, int newProtection, WeaponTag tag,
            PotionEffect effect, int strength, int buyValue, Sprite newIcon, Sprite newStack)
        {
            category = newCategory;
            power = newPower;
            protection = newProtection;
            weaponTag = tag;
            potionEffect = effect;
            potionStrength = strength;
            traderBuyValue = buyValue;
            icon = newIcon;
            stackSprite = newStack;
        }
#endif
    }
}
