using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>
    /// A camp on the journey to the castle (Docs/JOURNEY_OUTLINE.md, 2026-10-15): its map is a clearing (where the
    /// wagon, shop and stations stand) wrapped by forest and hills on its two low sides and around its side tips, with the
    /// cliff along its two high sides. The forest's bands run outward from the clearing: the further out, the newer the
    /// wood and the deeper the ore in the hills.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Camp", fileName = "camp_")]
    public sealed class CampDefinition : ContentDefinition
    {
        [Min(1)] [SerializeField] private int order = 1;
        [Tooltip("The clearing, in map cells (x right-up, y left-up). Its two high edges are the cliff.")]
        [SerializeField] private RectInt clearing = new RectInt(-20, -16, 48, 37);
        [Tooltip("How many cells of forest wrap the clearing's low sides.")]
        [Min(4)] [SerializeField] private int forestDepth = 20;
        [Tooltip("Cells per forest band (each band: the next wood, and the next of the Mine's layers in the hills).")]
        [Min(1)] [SerializeField] private int bandWidth = 7;
        [Tooltip("Flat cells beside the clearing before the hills rise.")]
        [Min(0)] [SerializeField] private int flatMargin = 3;

        public override string IdKind => "camp";
        public int Order => order;
        public RectInt Clearing => clearing;
        public int ForestDepth => forestDepth;
        public int BandWidth => bandWidth;
        public int FlatMargin => flatMargin;

        /// <summary>The whole camp: the clearing plus the forest on its low sides.</summary>
        public RectInt Bounds => new RectInt(clearing.xMin - forestDepth, clearing.yMin - forestDepth, clearing.width + forestDepth, clearing.height + forestDepth);

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (clearing.width < 8 || clearing.height < 8) errors.Add($"{Describe()}: clearing too small");
        }

#if UNITY_EDITOR
        public void EditorSetup(int campOrder, RectInt clear, int depth, int bands, int flat)
        {
            order = campOrder;
            clearing = clear;
            forestDepth = depth;
            bandWidth = bands;
            flatMargin = flat;
        }
#endif
    }
}
