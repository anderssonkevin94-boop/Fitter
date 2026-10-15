using System;
using System.Collections.Generic;

namespace Ravenholm.Domain.Camp
{
    /// <summary>Where a station is: where the camp was laid out, set up somewhere the player chose, or packed into its kit.</summary>
    public enum StationSiteMode
    {
        Default,
        Placed,
        Packed,
    }

    /// <summary>
    /// A station's spot at the camp (journey step 2, 2026-10-15): its footprint is <see cref="W"/> x <see cref="H"/> map
    /// cells from (<see cref="X"/>, <see cref="Y"/>) (x right-up, y left-up). Setting it up somewhere takes a short build,
    /// until <see cref="ReadyAtUtcTicks"/>.
    /// </summary>
    public sealed class StationSite
    {
        public string StationId;
        public StationSiteMode Mode;
        public int X, Y;
        public int W = 2, H = 2;
        public long ReadyAtUtcTicks;

        public bool Standing => Mode != StationSiteMode.Packed;

        public bool Covers(int x, int y) => Standing && x >= X && x < X + W && y >= Y && y < Y + H;

        public bool Overlaps(int x, int y, int w, int h) => Standing && x < X + W && X < x + w && y < Y + H && Y < y + h;
    }

    public sealed class CampStationsState
    {
        public readonly Dictionary<string, StationSite> Sites = new Dictionary<string, StationSite>();
    }

    /// <summary>The placement rules, given what the ground is like at each cell (engine-free).</summary>
    public static class StationPlacement
    {
        /// <summary>
        /// Why a station can't stand with its corner at (x, y) (null: it can). <paramref name="groundProblem"/> says why a
        /// cell can't be built on (null: cleared, level ground).
        /// </summary>
        public static string CanPlace(CampStationsState state, StationSite site, int x, int y, Func<int, int, string> groundProblem)
        {
            foreach (var other in state.Sites.Values)
                if (other != site && other.Overlaps(x, y, site.W, site.H)) return "Too close to another station";
            for (var cx = x; cx < x + site.W; cx++)
            for (var cy = y; cy < y + site.H; cy++)
            {
                var why = groundProblem(cx, cy);
                if (why != null) return why;
            }
            return null;
        }
    }
}
