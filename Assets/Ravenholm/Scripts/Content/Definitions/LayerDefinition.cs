using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>A Mine depth band (GDD 6).</summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Mine Layer Definition", fileName = "layer_")]
    public sealed class LayerDefinition : SiteBandDefinition
    {
        public override string IdKind => "layer";
    }
}
