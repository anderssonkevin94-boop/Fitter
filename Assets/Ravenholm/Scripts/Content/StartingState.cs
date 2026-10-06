using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>What a brand-new company owns (GDD 15 balance starting point). Starter blueprints are flagged on the blueprints.</summary>
    [CreateAssetMenu(menuName = "Ravenholm/Config/Starting State", fileName = "StartingState")]
    public sealed class StartingState : ScriptableObject
    {
        [Tooltip("Shared usable materials at the start (GDD 15: 6 iron ingots, 6 cedar lumber, 1 copper ingot, 2 herbs).")]
        [SerializeField] private List<ResourceCost> sharedStock = new List<ResourceCost>();

        public IReadOnlyList<ResourceCost> SharedStock => sharedStock;

#if UNITY_EDITOR
        public void EditorSetup(List<ResourceCost> stock) => sharedStock = stock;
#endif
    }
}
