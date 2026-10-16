using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>What a piece of shop furniture shows: nothing (the desk), or a kind of stock.</summary>
    public enum FurnitureDisplay
    {
        None,
        Armor,
        Potion,
        Small,
        Weapon,
        Long,
        Any,
    }

    /// <summary>
    /// A type of shop furniture the player owns, places, rotates and upgrades (Shop Titans style, 2026-10-15). Each level
    /// has a floor footprint in whole tiles (facing A as drawn; facing B swaps it), or hangs across wall edges, plus its
    /// art for both facings (or both walls) with ground points and item-slot boxes in the sprite's pixels (top-left
    /// origin). A missing facing-B sprite falls back to facing A mirrored (placeholder until the art exists).
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Shop Furniture", fileName = "furniture_")]
    public sealed class ShopFurnitureDefinition : ContentDefinition
    {
        [Serializable]
        public sealed class Art
        {
            public Sprite sprite;
            [Tooltip("The sprite's ground point (its pivot) in pixels from the top-left.")]
            public Vector2 ground;
            [Tooltip("Item-slot boxes in pixels from the sprite's top-left.")]
            public List<Rect> slots = new List<Rect>();
            [Tooltip("The armour stand: the support alone on the same canvas, drawn under body armour (the mannequin's torso would show at the armour's edges).")]
            public Sprite armored;
        }

        [Serializable]
        public sealed class Level
        {
            [Tooltip("Floor footprint in tiles (facing A). Ignored for wall pieces.")]
            public Vector2Int footprint = Vector2Int.one;
            public Art facingA = new Art();
            public Art facingB = new Art();
            [Tooltip("Cost to upgrade TO this level (level 1: the price of a new piece).")]
            [Min(0)] public int gold;
            public List<ResourceCost> materials = new List<ResourceCost>();
        }

        [SerializeField] private FurnitureDisplay display;
        [SerializeField] private bool wall;
        [Min(1)] [SerializeField] private int wallEdges = 2;
        [SerializeField] private float pixelsPerUnit = 256f;
        [Tooltip("Can be bought more than once (the desk cannot).")]
        [SerializeField] private bool forSale = true;
        [SerializeField] private List<Level> levels = new List<Level>();
        [Header("The wagon shop (the journey, step 3)")]
        [Tooltip("A wall piece's free-standing version: on the wagon's carpet (no walls) the piece becomes this, at its level.")]
        [SerializeField] private ShopFurnitureDefinition standingVersion;
        [Tooltip("Not used at the wagon (the desk: the wagon's counter does its job). Upgrades paid for it are refunded.")]
        [SerializeField] private bool retiredByWagon;

        public override string IdKind => "furniture";
        public FurnitureDisplay Display => display;
        public bool Wall => wall;
        public int WallEdges => wallEdges;
        public float PixelsPerUnit => pixelsPerUnit;
        public bool ForSale => forSale;
        public IReadOnlyList<Level> Levels => levels;
        public int MaxLevel => levels.Count;
        public ShopFurnitureDefinition StandingVersion => standingVersion;
        public bool RetiredByWagon => retiredByWagon;

        public Level At(int level) => levels.Count == 0 ? null : levels[Mathf.Clamp(level - 1, 0, levels.Count - 1)];

        /// <summary>Whether an item is shown on this piece.</summary>
        public bool Shows(ItemDefinition item) => display switch
        {
            FurnitureDisplay.Armor => item.IsArmourLike,   // body armour, helmets and shields
            FurnitureDisplay.Potion => item.IsConsumable,   // potions and food
            FurnitureDisplay.Small => item.SmallDisplay,
            FurnitureDisplay.Weapon => item.Category == ItemCategory.Weapon && !item.SmallDisplay && item.WeaponTag != WeaponTag.Bow && item.WeaponTag != WeaponTag.Staff,
            FurnitureDisplay.Long => item.WeaponTag == WeaponTag.Bow || item.WeaponTag == WeaponTag.Staff,
            FurnitureDisplay.Any => true,
            _ => false,
        };

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (levels.Count == 0) errors.Add($"{Describe()}: no levels");
            for (var i = 0; i < levels.Count; i++)
            {
                if (levels[i].facingA.sprite == null) errors.Add($"{Describe()}: level {i + 1} has no art");
                if (!wall && (levels[i].footprint.x < 1 || levels[i].footprint.y < 1)) errors.Add($"{Describe()}: level {i + 1} footprint is empty");
            }
        }

#if UNITY_EDITOR
        public void EditorSetWagon(ShopFurnitureDefinition standing, bool retired, bool sale)
        {
            standingVersion = standing;
            retiredByWagon = retired;
            forSale = sale;
        }

        public void EditorSetup(FurnitureDisplay d, bool isWall, int edges, float ppu, bool sale, List<Level> l)
        {
            forSale = sale;
            display = d;
            wall = isWall;
            wallEdges = edges;
            pixelsPerUnit = ppu;
            levels = l;
        }
#endif
    }
}
