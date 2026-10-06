using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>A persistent named worker (GDD 5, locked: identity and equipment only; no XP, wages or needs).</summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Worker Definition", fileName = "worker_")]
    public sealed class WorkerDefinition : ContentDefinition
    {
        [Tooltip("Seed for future appearance variation (portrait, palette).")]
        [SerializeField] private int appearanceSeed;
        [SerializeField] private WorkerEquipmentDefinition startingTool;
        [SerializeField] private WorkerEquipmentDefinition startingPack;

        public override string IdKind => "worker";
        public int AppearanceSeed => appearanceSeed;
        public WorkerEquipmentDefinition StartingTool => startingTool;
        public WorkerEquipmentDefinition StartingPack => startingPack;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (startingTool == null || startingTool.Slot != EquipmentSlot.Tool) errors.Add($"{Describe()}: starting tool missing or not a tool");
            if (startingPack == null || startingPack.Slot != EquipmentSlot.Pack) errors.Add($"{Describe()}: starting pack missing or not a pack");
        }

#if UNITY_EDITOR
        public void EditorSetupWorker(int seed, WorkerEquipmentDefinition tool, WorkerEquipmentDefinition pack)
        {
            appearanceSeed = seed;
            startingTool = tool;
            startingPack = pack;
        }
#endif
    }
}
