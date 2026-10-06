using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>
    /// A quest-map region (GDD 14): a label and theme for a group of authored quest nodes. Regions are
    /// presentation and validation structure only; unlocks still come from each quest's prerequisites.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Region Definition", fileName = "region_")]
    public sealed class RegionDefinition : ContentDefinition
    {
        [Min(0)] [SerializeField] private int order;
        [TextArea(1, 3)] [SerializeField] private string theme = "";
        [Tooltip("Shown while the region is still a silhouette on the map.")]
        [TextArea(1, 2)] [SerializeField] private string rumour = "";

        public override string IdKind => "region";
        public int Order => order;
        public string Theme => theme;
        public string Rumour => rumour;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (string.IsNullOrWhiteSpace(rumour)) errors.Add($"{Describe()}: needs a rumour line for the map silhouette");
        }

#if UNITY_EDITOR
        public void EditorSetupRegion(int newOrder, string newTheme, string newRumour)
        {
            order = newOrder; theme = newTheme; rumour = newRumour;
        }
#endif
    }
}
