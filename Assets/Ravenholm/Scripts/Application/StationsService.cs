using System;
using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Camp;
using Ravenholm.Domain.Sites;
using Ravenholm.Domain.Time;
using Ravenholm.Infrastructure.Save;

namespace Ravenholm.Services
{
    /// <summary>
    /// The stations at camp (the journey, step 2, Kevin 2026-10-15): each stands where the camp was laid out until the
    /// player sets it up somewhere else, on cleared, level ground: the clearing, or forest ground with no tree or stump
    /// and its hill dug flat. Setting one up takes a short build. A station can be packed into its kit (its output pad
    /// empty) and set up again; processing carries on wherever it is. Owns the "stations" save section.
    /// </summary>
    public sealed class StationsService : ISaveSection
    {
        private readonly SaveScheduler _scheduler;
        private readonly ContentCatalogue _catalogue;
        private readonly SitesService _sites;
        private readonly LogisticsService _logistics;
        private readonly IClock _clock;
        private readonly float _buildSeconds;

        public StationsService(SaveScheduler scheduler, ContentCatalogue catalogue, SitesService sites, LogisticsService logistics, IClock clock,
            float buildSeconds)
        {
            _scheduler = scheduler;
            _catalogue = catalogue;
            _sites = sites;
            _logistics = logistics;
            _clock = clock;
            _buildSeconds = buildSeconds;
            scheduler.Register(this);
        }

        public CampStationsState State { get; private set; } = new CampStationsState();

        /// <summary>Raised after a station moved, was packed or set up.</summary>
        public event Action Changed;

        /// <summary>Cells other things stand on (the shop's lot, the Trader's pad): set by the scene.</summary>
        public Func<int, int, bool> OccupiedByOthers;

        public StationSite Site(string stationId) => stationId != null && State.Sites.TryGetValue(stationId, out var s) ? s : null;

        /// <summary>The scene says where a station was laid out (its corner cell); saved spots win over it.</summary>
        public StationSite RegisterDefault(string stationId, int x, int y, int w, int h)
        {
            if (!State.Sites.TryGetValue(stationId, out var s))
            {
                // A station that arrives as a kit (the Kitchen) waits to be set up.
                var kit = _catalogue.TryGet<StationDefinition>(stationId, out var def) && def.StartsAsKit;
                State.Sites[stationId] = s = new StationSite { StationId = stationId, Mode = kit ? StationSiteMode.Packed : StationSiteMode.Default };
            }
            s.W = w;
            s.H = h;
            if (s.Mode == StationSiteMode.Default) { s.X = x; s.Y = y; }
            return s;
        }

        public bool Building(string stationId) => Site(stationId) is StationSite s && s.Standing && _clock.UtcNow.Ticks < s.ReadyAtUtcTicks;

        /// <summary>How far through its build a station is (1 = ready).</summary>
        public float BuildProgress(string stationId)
        {
            var s = Site(stationId);
            if (s == null || s.ReadyAtUtcTicks == 0) return 1f;
            var total = TimeSpan.FromSeconds(Math.Max(0.01, _buildSeconds)).Ticks;
            var left = s.ReadyAtUtcTicks - _clock.UtcNow.Ticks;
            return left <= 0 ? 1f : 1f - (float)left / total;
        }

        // ------------------------------------------------------------------ the ground

        /// <summary>Why a station can't stand on a map cell (null: cleared, level ground of the camp's land).</summary>
        public string GroundProblem(int x, int y)
        {
            var camp = _catalogue.CurrentCamp;
            if (camp == null) return "No camp";
            var ridge = camp.Ridge;
            if (!ridge.InLand(x, y)) return "Not on the land";
            if (OccupiedByOthers != null && OccupiedByOthers(x, y)) return "Something else stands there";
            var forest = _sites.Get("site.forest");
            if (forest == null || !forest.GroveMode || !forest.Grove.InGrove(x, y)) return null;   // the clearing
            if (forest.CanChop(x, y)) return "Clear the trees first";
            if (forest.IsStump(x, y)) return "Pull the stumps out first";
            var mine = _sites.Get("site.mine");
            for (var level = forest.TerrainHeight(x, y); level >= 1; level--)
                if (mine == null || !mine.IsSurfaceBroken(x, y, level)) return "Dig the ground level first";
            return null;
        }

        /// <summary>Why the land at a cell can't take something new (the wagon's carpet): the ground, or a station on it.</summary>
        public string LandProblem(int x, int y)
        {
            var others = OccupiedByOthers;
            OccupiedByOthers = null;
            try
            {
                var why = GroundProblem(x, y);
                if (why != null) return why;
            }
            finally { OccupiedByOthers = others; }
            foreach (var s in State.Sites.Values) if (s.Covers(x, y)) return "A station stands there";
            return null;
        }

        public string CanPlace(string stationId, int x, int y)
        {
            var s = Site(stationId);
            return s == null ? "Unknown station" : StationPlacement.CanPlace(State, s, x, y, GroundProblem);
        }

        // ------------------------------------------------------------------ commands

        /// <summary>Sets a station up with its corner at (x, y): from where it stands, or from its kit. It takes a short build.</summary>
        public string Place(string stationId, int x, int y)
        {
            var why = CanPlace(stationId, x, y);
            if (why != null) return why;
            var s = Site(stationId);
            if (s.Standing && s.X == x && s.Y == y) return null;
            s.Mode = StationSiteMode.Placed;
            s.X = x;
            s.Y = y;
            s.ReadyAtUtcTicks = _clock.UtcNow.Ticks + TimeSpan.FromSeconds(_buildSeconds).Ticks;
            Commit("set up a station");
            return null;
        }

        public string PackBlockedReason(string stationId)
        {
            var s = Site(stationId);
            if (s == null || !s.Standing) return "Already packed";
            var def = _catalogue.TryGet<StationDefinition>(stationId, out var d) ? d : null;
            var pad = def != null ? def.OutputPadContainerId : null;
            if (!string.IsNullOrEmpty(pad) && _logistics.System.State.Containers.TryGetValue(pad, out var c) && c.UnitCount > 0)
                return "Collect the goods from its pad first";
            return null;
        }

        /// <summary>Packs a station into its kit (to be set up again later).</summary>
        public string Pack(string stationId)
        {
            var why = PackBlockedReason(stationId);
            if (why != null) return why;
            var s = Site(stationId);
            s.Mode = StationSiteMode.Packed;
            s.ReadyAtUtcTicks = 0;
            Commit("packed a station");
            return null;
        }

        public IEnumerable<StationSite> Kits()
        {
            foreach (var s in State.Sites.Values) if (!s.Standing) yield return s;
        }

        private void Commit(string reason)
        {
            _scheduler.SaveNow(reason);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ save

        void ISaveSection.Write(SaveEnvelope env)
        {
            var d = new StationsSaveData { Camp = _catalogue.CurrentCamp?.Id };
            foreach (var s in State.Sites.Values)
            {
                if (s.Mode == StationSiteMode.Default) continue;
                d.Sites.Add(new StationSiteSaveData { Id = s.StationId, Packed = s.Mode == StationSiteMode.Packed, X = s.X, Y = s.Y, ReadyAt = s.ReadyAtUtcTicks });
            }
            env.Stations = d;
        }

        void ISaveSection.Read(SaveEnvelope env)
        {
            // Keep the scene's layout (registered defaults) and replace what the save changed.
            var fresh = new CampStationsState();
            foreach (var kv in State.Sites)
                fresh.Sites[kv.Key] = new StationSite { StationId = kv.Key, W = kv.Value.W, H = kv.Value.H,
                    Mode = _catalogue.TryGet<StationDefinition>(kv.Key, out var kd) && kd.StartsAsKit ? StationSiteMode.Packed : StationSiteMode.Default,
                    X = kv.Value.Mode == StationSiteMode.Default ? kv.Value.X : 0, Y = kv.Value.Mode == StationSiteMode.Default ? kv.Value.Y : 0 };
            var d = env?.Stations;
            if (d != null && d.Camp == _catalogue.CurrentCamp?.Id)
                foreach (var sd in d.Sites)
                {
                    if (string.IsNullOrEmpty(sd.Id)) continue;
                    if (!fresh.Sites.TryGetValue(sd.Id, out var s)) fresh.Sites[sd.Id] = s = new StationSite { StationId = sd.Id };
                    s.Mode = sd.Packed ? StationSiteMode.Packed : StationSiteMode.Placed;
                    s.X = sd.X;
                    s.Y = sd.Y;
                    s.ReadyAtUtcTicks = sd.ReadyAt;
                }
            State = fresh;
            Changed?.Invoke();
        }
    }
}
