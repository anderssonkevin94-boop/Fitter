using Ravenholm.Content;
using Ravenholm.Domain.Logistics;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>Resolves carry units to their authored sprites and names through the catalogue.</summary>
    public static class UnitVisuals
    {
        public static Sprite StackSprite(CarryUnit unit)
        {
            var root = GameRoot.Instance;
            if (root == null) return null;
            var cat = root.Services.Catalogue;
            if (unit.Kind == UnitKind.Material)
                return cat.TryGet<ResourceDefinition>(unit.Id, out var r) ? r.StackSprite : null;
            var def = root.Services.Logistics.System.DefinitionOf(unit);
            return def != null && cat.TryGet<ItemDefinition>(def, out var i) ? i.StackSprite : null;
        }

        public static string DisplayName(string definitionId)
        {
            var root = GameRoot.Instance;
            return root != null && root.Services.Catalogue.TryGet<ContentDefinition>(definitionId, out var d) ? d.DisplayName : definitionId;
        }
    }
}
