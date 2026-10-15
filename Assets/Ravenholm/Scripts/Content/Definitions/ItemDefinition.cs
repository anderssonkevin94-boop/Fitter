using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>
    /// Player-crafted adventuring goods. GDD 8 had weapons, armour and potions; the crafting expansion (Kevin, 2026-10-16)
    /// adds helmets and shields (appended, so saved values stay put).
    /// </summary>
    public enum ItemCategory
    {
        Weapon,
        Armor,
        Potion,
        Helmet,
        Shield,
        /// <summary>Cooked at the Kitchen; carried in a consumable slot and always eaten on the trip.</summary>
        Food,
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
        // Phase 7 (appended to keep saved enum values stable)
        TrapsCounter,
        ExplorationCounter,
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

        [Tooltip("Weapons only: needs both hands, so no shield (mauls, bows, staves).")]
        [SerializeField] private bool twoHanded;
        [Tooltip("Shields only: the chance (percent) of blocking a killing blow, turning a death into a wounded retreat.")]
        [Range(0, 60)] [SerializeField] private int block;
        [Tooltip("Food only: survival and readiness from being well fed on the trip.")]
        [Min(0)] [SerializeField] private int foodSurvival;
        [Min(0)] [SerializeField] private int foodReadiness;

        [Tooltip("Gold the periodic Trader pays for one unit. Deliberately poor (GDD 15).")]
        [Min(0)] [SerializeField] private int traderBuyValue;

        [SerializeField] private Sprite icon;
        [SerializeField] private Sprite stackSprite;
        [Tooltip("Height between this piece and the one above it in a carried pile (local units before the pile's scale). Thin pieces (planks, blades, bows) stack closer. From the art's suggested-spacing.json.")]
        [Min(0.01f)] [SerializeField] private float stackStep = 0.13f;
        [Tooltip("A small piece (daggers, a focus): shown on the Store's display tables rather than its racks.")]
        [SerializeField] private bool smallDisplay;
        [Tooltip("Shown on shop furniture (Astra, 2026-10-15): view A on left-facing pieces, B on right-facing ones.")]
        [SerializeField] private Sprite displayA;
        [SerializeField] private Sprite displayB;
        [Tooltip("The views' visible bounds in pixels from the top-left (items are fitted by these, bottom-centre).")]
        [SerializeField] private Rect displayBoundsA;
        [SerializeField] private Rect displayBoundsB;

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
        public bool TwoHanded => twoHanded;
        public int Block => block;
        public int FoodSurvival => foodSurvival;
        public int FoodReadiness => foodReadiness;
        /// <summary>Goes in a consumable slot: potions and food.</summary>
        public bool IsConsumable => category == ItemCategory.Potion || category == ItemCategory.Food;
        /// <summary>Shown with the armour (stands and the Store's armour tab): body armour, helmets and shields.</summary>
        public bool IsArmourLike => category == ItemCategory.Armor || category == ItemCategory.Helmet || category == ItemCategory.Shield;

        /// <summary>Short stat line for cards, e.g. "Power 10 · arcane counter +4".</summary>
        /// <summary>The threat tag a counter potion answers (None for other items).</summary>
        public CounterTag PotionCounterTag =>
            category != ItemCategory.Potion ? CounterTag.None
            : potionEffect == PotionEffect.PoisonCounter ? CounterTag.Poison
            : potionEffect == PotionEffect.ArcaneCounter ? CounterTag.Arcane
            : potionEffect == PotionEffect.TrapsCounter ? CounterTag.Traps
            : potionEffect == PotionEffect.ExplorationCounter ? CounterTag.Exploration
            : CounterTag.None;

        public string StatLine()
        {
            string main;
            switch (category)
            {
                case ItemCategory.Weapon: main = $"Power {power} · {Readable(weaponTag.ToString())} weapon{(twoHanded ? ", two-handed" : "")}"; break;
                case ItemCategory.Armor: main = $"Protection {protection}"; break;
                case ItemCategory.Helmet: return $"Helmet · guards against {Readable(counterTag.ToString()).ToLowerInvariant()} +{counterStrength}";
                case ItemCategory.Shield: main = $"Shield · blocks {block}% of killing blows"; break;
                case ItemCategory.Food: return $"Food · well fed: +{foodSurvival} survival, +{foodReadiness} readiness (always eaten)";
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
        public float StackStep => stackStep;
        public bool SmallDisplay => smallDisplay;

        /// <summary>The display view for a facing (A for left-facing furniture), with its visible bounds; null if not drawn.</summary>
        public Sprite DisplaySprite(bool facingB, out Rect visibleBounds)
        {
            visibleBounds = facingB ? displayBoundsB : displayBoundsA;
            return facingB ? displayB : displayA;
        }

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
                case ItemCategory.Helmet:
                    if (counterTag == CounterTag.None || counterStrength <= 0) errors.Add($"{Describe()}: a helmet guards against a threat (counter > 0)");
                    if (power != 0 || protection != 0 || weaponTag != WeaponTag.None || potionEffect != PotionEffect.None) errors.Add($"{Describe()}: helmet has other fields set");
                    break;
                case ItemCategory.Shield:
                    if (block <= 0) errors.Add($"{Describe()}: a shield needs a block chance");
                    if (power != 0 || protection != 0 || weaponTag != WeaponTag.None || potionEffect != PotionEffect.None) errors.Add($"{Describe()}: shield has other fields set");
                    break;
                case ItemCategory.Food:
                    if (foodSurvival <= 0 && foodReadiness <= 0) errors.Add($"{Describe()}: food needs survival or readiness");
                    if (power != 0 || protection != 0 || weaponTag != WeaponTag.None || potionEffect != PotionEffect.None) errors.Add($"{Describe()}: food has other fields set");
                    break;
                case ItemCategory.Potion:
                    if (potionEffect == PotionEffect.None || potionStrength <= 0) errors.Add($"{Describe()}: potion needs one effect with strength > 0");
                    if (power != 0 || protection != 0 || weaponTag != WeaponTag.None) errors.Add($"{Describe()}: potion has weapon/armor fields set");
                    break;
            }
        }

#if UNITY_EDITOR
        public void EditorSetPower(int p) => power = p;
        public void EditorSetSmallDisplay(bool small) => smallDisplay = small;

        public void EditorSetDisplay(Sprite a, Rect boundsA, Sprite b, Rect boundsB)
        {
            displayA = a;
            displayBoundsA = boundsA;
            displayB = b;
            displayBoundsB = boundsB;
        }

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
        public void EditorSetStackStep(float step) => stackStep = step;
        public void EditorSetHands(bool needsBoth) => twoHanded = needsBoth;
        public void EditorSetBlock(int percent) => block = percent;

        public void EditorSetFood(int survival, int readiness)
        {
            foodSurvival = survival;
            foodReadiness = readiness;
        }

        public void EditorSetProtection(int p) => protection = p;
#endif
    }
}
