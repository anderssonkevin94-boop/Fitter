using System;
using System.Collections.Generic;
using Ravenholm.Content;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>What a piece of Store furniture shows: nothing (the desk), armour, potions, or small pieces.</summary>
    public enum FurnitureDisplay
    {
        None,
        Armor,
        Potion,
        Small,
    }

    /// <summary>
    /// A piece of Store furniture in its upgrade levels (Astra's shop furniture, 2026-10-15). Each level has its own
    /// sprite and item-slot boxes in the sprite's pixels (top-left origin), measured from the sprite's ground point.
    /// The level shown is the Store station's level. Built by setup from the art's points.json; never edited at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Config/Store Furniture", fileName = "furniture_")]
    public sealed class StoreFurnitureDefinition : ScriptableObject
    {
        [Serializable]
        public sealed class Level
        {
            public Sprite sprite;
            [Tooltip("The sprite's ground point (its pivot) in pixels from the top-left.")]
            public Vector2 ground;
            [Tooltip("Item-slot boxes in pixels from the sprite's top-left.")]
            public List<Rect> slots = new List<Rect>();
            [Tooltip("Floor footprint that blocks walking, in ground tiles along the room's two axes (0 = none).")]
            public Vector2 footprint;
        }

        [SerializeField] private FurnitureDisplay display;
        [SerializeField] private StationDefinition levelFrom;
        [SerializeField] private float pixelsPerUnit = 256f;
        [SerializeField] private List<Level> levels = new List<Level>();

        public FurnitureDisplay Display => display;
        public StationDefinition LevelFrom => levelFrom;
        public float PixelsPerUnit => pixelsPerUnit;
        public IReadOnlyList<Level> Levels => levels;

        /// <summary>The level shown for a station level (clamped to the levels drawn).</summary>
        public Level At(int stationLevel) => levels.Count == 0 ? null : levels[Mathf.Clamp(stationLevel - 1, 0, levels.Count - 1)];

        /// <summary>Whether an item is shown on this piece.</summary>
        public bool Shows(ItemDefinition item) => display switch
        {
            FurnitureDisplay.Armor => item.Category == ItemCategory.Armor,
            FurnitureDisplay.Potion => item.Category == ItemCategory.Potion,
            FurnitureDisplay.Small => item.SmallDisplay,
            _ => false,
        };

#if UNITY_EDITOR
        public void EditorSetup(FurnitureDisplay d, StationDefinition from, float ppu, List<Level> l)
        {
            display = d;
            levelFrom = from;
            pixelsPerUnit = ppu;
            levels = l;
        }
#endif
    }
}
