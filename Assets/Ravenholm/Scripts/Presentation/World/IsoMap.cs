using Ravenholm.Domain.Sites;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Map cells and the world, one mapping for everything that draws or walks (the switchback pass, 2026-10-16): the
    /// isometric grid lowered by each leg's drop (<see cref="RidgeLayout.CellToWorld"/>). The ground plane stays one flat
    /// plane to walk on; a leg further down the pass simply lies lower on it. Without a pass it is the plain grid.
    /// </summary>
    public static class IsoMap
    {
        /// <summary>The land in use (set when the game boots, and by the scene builder).</summary>
        public static RidgeLayout Ridge { get; set; }

        /// <summary>The world point of a cell's centre (fractional cells allowed).</summary>
        public static Vector2 World(float cx, float cy)
        {
            if (Ridge == null) return new Vector2((cx - cy) * 0.5f, (cx + cy) * 0.25f + 0.25f);
            var (x, y) = Ridge.CellToWorld(cx, cy);
            return new Vector2((float)x, (float)y);
        }

        public static Vector2 World(Vector3Int c) => World(c.x, c.y);

        /// <summary>The fractional cell at a world point (cell centres on whole numbers).</summary>
        public static Vector2 CellF(Vector2 world)
        {
            if (Ridge == null) return new Vector2(world.x + 2f * (world.y - 0.25f), 2f * (world.y - 0.25f) - world.x);
            var (cx, cy) = Ridge.WorldToCell(world.x, world.y);
            return new Vector2((float)cx, (float)cy);
        }

        /// <summary>The cell a world point falls in (as the grid's own WorldToCell, with the drop undone).</summary>
        public static Vector3Int Cell(Vector2 world)
        {
            var c = CellF(world);
            return new Vector3Int(Mathf.FloorToInt(c.x + 0.5f), Mathf.FloorToInt(c.y + 0.5f), 0);
        }

        /// <summary>How far down a cell is drawn (world units; 0 off the pass).</summary>
        public static float Drop(Vector3Int c) => Ridge != null ? (float)Ridge.DropAtCell(c.x, c.y) : 0f;
    }
}
