using System;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>
    /// An object drawn from four sides at the game's elevated view (Astra's four item views, 2026-10-17): A and B the two
    /// diagonals, front its main face (logs, lumber and ingots end-on), side a quarter-turn from it (their long axis across
    /// the screen). 256 px per unit, the pivot on the display anchor. A and B are named for the furniture they sit on, not
    /// the way the item lies, so which of them runs up to the right is measured per item (<see cref="aRunsUpRight"/>).
    /// </summary>
    [Serializable]
    public sealed class ObjectViews
    {
        public Sprite a, front, b, side;
        [Tooltip("View A's long side climbs to the right on screen (B's to the left); measured from the art at import.")]
        public bool aRunsUpRight = true;

        public bool Any => a != null || front != null || b != null || side != null;

        /// <summary>
        /// The view for a piece carried crosswise by someone facing <paramref name="dir"/> (E, NE, N, NW, W, SW, S, SE on
        /// screen): its long axis across their way, so facing the camera or away it lies across the screen (side), facing
        /// left or right it points at the camera (front), and on the diagonals it runs along the other diagonal (A or B).
        /// Falls back to any view drawn.
        /// </summary>
        public Sprite Crosswise(string dir)
        {
            Sprite want = dir switch
            {
                "N" or "S" => side,
                "E" or "W" => front,
                "SE" or "NW" => aRunsUpRight ? a : b,   // across his way: up to the right
                "NE" or "SW" => aRunsUpRight ? b : a,   // across his way: up to the left
                _ => side,
            };
            return want != null ? want : front != null ? front : side != null ? side : a != null ? a : b;
        }
    }
}
