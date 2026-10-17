using System.Text.RegularExpressions;
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

        /// <summary>
        /// A carried piece seen the way its carrier faces (Astra's four views, 2026-10-17), null when it has none: the
        /// pile then shows its single <see cref="StackSprite"/>.
        /// </summary>
        public static Sprite CarryView(CarryUnit unit, string dir)
        {
            var root = GameRoot.Instance;
            if (root == null) return null;
            var cat = root.Services.Catalogue;
            Content.ObjectViews v = null;
            if (unit.Kind == UnitKind.Material) { if (cat.TryGet<ResourceDefinition>(unit.Id, out var r)) v = r.Views; }
            else
            {
                var def = root.Services.Logistics.System.DefinitionOf(unit);
                if (def != null && cat.TryGet<ItemDefinition>(def, out var i)) v = i.Views;
            }
            return v != null && v.Any ? v.Crosswise(dir) : null;
        }

        /// <summary>The views are drawn at 256 px per unit; the single stack sprites at 512: a view's scale in the pile.</summary>
        public const float CarryViewScale = 0.5f;

        /// <summary>The height a piece takes in a pile (its definition's stack step; 0.13 when unknown).</summary>
        public static float StackStep(CarryUnit unit)
        {
            var root = GameRoot.Instance;
            if (root == null) return DefaultStackStep;
            var cat = root.Services.Catalogue;
            if (unit.Kind == UnitKind.Material)
                return cat.TryGet<ResourceDefinition>(unit.Id, out var r) ? r.StackStep : DefaultStackStep;
            var def = root.Services.Logistics.System.DefinitionOf(unit);
            return def != null && cat.TryGet<ItemDefinition>(def, out var i) ? i.StackStep : DefaultStackStep;
        }

        public const float DefaultStackStep = 0.13f;

        /// <summary>
        /// Heights of the pieces in a pile, bottom first (local units): each piece sits on top of the one below, which
        /// is as high as that lower piece's step. Thin pieces (planks, blades, bows) stack closely, and a bulky piece on a
        /// thin one sits just above it. Tuned in game, 2026-10-14: Astra's suggested starting rule, the smaller of the two
        /// steps, sank bulky pieces into thin ones and thin pieces into bulky ones.
        /// Returns the height of the top of the pile (where a next piece would sit).
        /// </summary>
        public static float PileHeights(System.Collections.Generic.IReadOnlyList<CarryUnit> units, float[] heights)
        {
            var y = 0f;
            var prev = 0f;
            for (var i = 0; i < units.Count; i++)
            {
                var step = StackStep(units[i]);
                if (i > 0) y += prev;
                if (heights != null && i < heights.Length) heights[i] = y;
                prev = step;
            }
            return units.Count > 0 ? y + prev : 0f;
        }

        private static readonly Regex IdPattern = new Regex(@"\b[a-z]+\.[a-z0-9_]+\b");

        /// <summary>Replaces content IDs inside a message (e.g. a blocked reason) with display names.</summary>
        public static string PrettyIds(string text) =>
            string.IsNullOrEmpty(text) ? text : IdPattern.Replace(text, m => DisplayName(m.Value));

        public static string DisplayName(string definitionId)
        {
            var root = GameRoot.Instance;
            return root != null && root.Services.Catalogue.TryGet<ContentDefinition>(definitionId, out var d) ? d.DisplayName : definitionId;
        }
    }
}
