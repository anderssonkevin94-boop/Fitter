using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>A Forest area (GDD 7).</summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Forest Zone Definition", fileName = "zone_")]
    public sealed class ForestZoneDefinition : SiteBandDefinition
    {
        public override string IdKind => "zone";
    }
}
