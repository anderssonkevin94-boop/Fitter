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

        public override string IdKind => "station";
        public string OutputPadContainerId => outputPadContainerId;
        public int OutputPadCapacity => outputPadCapacity;
        public int StartingLevel => startingLevel;
        public int MaxLevel => maxLevel;
        public float BatchTimePerLevel => batchTimePerLevel;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (maxLevel < startingLevel) errors.Add($"{Describe()}: max level below starting level");
        }

#if UNITY_EDITOR
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
