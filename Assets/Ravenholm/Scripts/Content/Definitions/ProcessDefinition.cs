using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>Automatic timed conversion of delivered raw materials (GDD 8). Not crafting.</summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Process Definition", fileName = "process_")]
    public sealed class ProcessDefinition : ContentDefinition
    {
        [SerializeField] private StationDefinition facility;
        [Min(1)] [SerializeField] private int minFacilityLevel = 1;
        [SerializeField] private List<ResourceCost> inputs = new List<ResourceCost>();
        [SerializeField] private ResourceDefinition output;
        [Min(1)] [SerializeField] private int outputQuantity = 1;
        [Tooltip("Seconds per batch at facility level 1.")]
        [Min(0.1f)] [SerializeField] private float batchSeconds = 4f;

        public override string IdKind => "process";
        public StationDefinition Facility => facility;
        public int MinFacilityLevel => minFacilityLevel;
        public IReadOnlyList<ResourceCost> Inputs => inputs;
        public ResourceDefinition Output => output;
        public int OutputQuantity => outputQuantity;
        public float BatchSeconds => batchSeconds;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (facility == null) errors.Add($"{Describe()}: facility missing");
            if (output == null) errors.Add($"{Describe()}: output missing");
            if (inputs.Count == 0) errors.Add($"{Describe()}: needs at least one input");
            foreach (var c in inputs) if (c.resource == null || c.quantity <= 0) errors.Add($"{Describe()}: invalid input");
        }

#if UNITY_EDITOR
        public void EditorSetupProcess(StationDefinition f, int level, List<ResourceCost> ins, ResourceDefinition o, int qty, float seconds)
        {
            facility = f;
            minFacilityLevel = level;
            inputs = ins;
            output = o;
            outputQuantity = qty;
            batchSeconds = seconds;
        }
#endif
    }
}
