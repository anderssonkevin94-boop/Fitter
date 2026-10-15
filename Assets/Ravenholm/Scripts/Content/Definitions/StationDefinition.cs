using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>
    /// A production building: receiver, automatic processor and/or crafting station (GDD 8/9/16).
    /// Levels gate processes and recipes; upgrades themselves arrive in Phase 6.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Station Definition", fileName = "station_")]
    public sealed class StationDefinition : ContentDefinition
    {
        [Tooltip("World container ID of this station's output pad (crafting stations only).")]
        [SerializeField] private string outputPadContainerId = "";
        [Tooltip("Finished goods the output pad can hold before crafting is blocked (GDD 9 default 6).")]
        [Min(1)] [SerializeField] private int outputPadCapacity = 6;
        [Min(1)] [SerializeField] private int startingLevel = 1;
        [Min(1)] [SerializeField] private int maxLevel = 2;
        [Tooltip("Batch time multiplier per level above 1 (GDD 16: ×0.85 at level 2). Applies to the next batch.")]
        [Range(0.3f, 1f)] [SerializeField] private float batchTimePerLevel = 0.85f;
        [Tooltip("This station's panel offers the supplier's own tools and the site equipment that unlocks them (the Forge).")]
        [SerializeField] private bool sellsSupplierTools;
        [Tooltip("Map cells the building stands on, when it is set up at a camp (x right-up, y left-up).")]
        [SerializeField] private Vector2Int footprint = new Vector2Int(2, 2);
        [Tooltip("A new station arrives as a kit: the player sets it up (the Kitchen, 2026-10-16).")]
        [SerializeField] private bool startsAsKit;

        public override string IdKind => "station";
        public string OutputPadContainerId => outputPadContainerId;
        public int OutputPadCapacity => outputPadCapacity;
        public int StartingLevel => startingLevel;
        public int MaxLevel => maxLevel;
        public float BatchTimePerLevel => batchTimePerLevel;
        public bool SellsSupplierTools => sellsSupplierTools;
        public Vector2Int Footprint => footprint;
        public bool StartsAsKit => startsAsKit;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (maxLevel < startingLevel) errors.Add($"{Describe()}: max level below starting level");
        }

#if UNITY_EDITOR
        public void EditorSetMaxLevel(int max) => maxLevel = max;
        public void EditorSetSellsSupplierTools(bool sells) => sellsSupplierTools = sells;
        public void EditorSetFootprint(Vector2Int cells) => footprint = cells;
        public void EditorSetStartsAsKit(bool kit) => startsAsKit = kit;

        public void EditorSetupStation(string pad, int padCapacity, int start, int max)
        {
            outputPadContainerId = pad;
            outputPadCapacity = padCapacity;
            startingLevel = start;
            maxLevel = max;
        }
#endif
    }
}
