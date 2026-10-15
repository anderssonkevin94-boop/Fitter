using System;
using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Infrastructure.Save;
using UnityEngine;

namespace Ravenholm.Services
{
    /// <summary>
    /// Moving on (the journey, step 4, Kevin 2026-10-15): which camp on the road the company is at. Standing in the next
    /// camp's clearing (the way there leads through a gate that needs a better tool), the supplier can make camp there:
    /// the stations pack into their kits (their output pads empty), the shop folds up, and what the wagon carries (Store
    /// goods and materials) must fit its storage; the camp's group (the wagon shop, the Trader's pad, the applicants) is
    /// set down on the new clearing. What was cleared and dug stays behind. Owns the "camp" save section.
    /// </summary>
    public sealed class CampService : ISaveSection
    {
        private readonly SaveScheduler _scheduler;
        private readonly ContentCatalogue _catalogue;
        private readonly StationsService _stations;
        private readonly ShopService _shop;
        private readonly LogisticsService _logistics;
        private readonly JobsService _jobs;

        public CampService(SaveScheduler scheduler, ContentCatalogue catalogue, StationsService stations, ShopService shop, LogisticsService logistics,
            JobsService jobs)
        {
            _jobs = jobs;
            _scheduler = scheduler;
            _catalogue = catalogue;
            _stations = stations;
            _shop = shop;
            _logistics = logistics;
            scheduler.Register(this);
        }

        /// <summary>The camp the company is at: 0 is camp 1.</summary>
        public int Index { get; private set; }

        /// <summary>The road's build projects already built (gate indices): a bridge, a causeway.</summary>
        public readonly SortedSet<int> Built = new SortedSet<int>();

        /// <summary>Raised after a build project was built (its gate index).</summary>
        public event Action<int> ProjectBuilt;

        /// <summary>Raised after the company moved on (from, to).</summary>
        public event Action<int, int> Moved;

        /// <summary>Raised after a load or reset replaced the camp.</summary>
        public event Action Reloaded;

        private CampDefinition Road => _catalogue.CurrentCamp;

        public string Name => Road != null ? Road.CampName(Index) : "";
        public bool HasNext => Road != null && Index + 1 < Road.CampCount;
        public string NextName => HasNext ? Road.CampName(Index + 1) : "";

        /// <summary>World position of a map cell's centre (cells: x right-up, y left-up).</summary>
        public static Vector2 CellCentre(Vector2 cell) => new Vector2((cell.x - cell.y) * 0.5f, (cell.x + cell.y) * 0.25f + 0.25f);

        /// <summary>The camp's centre in the world.</summary>
        public Vector2 CentreOf(int index) => Road != null ? CellCentre(Road.CampCentre(index)) : Vector2.zero;

        /// <summary>How far the camp's group stands from where it was laid out at camp 1 (world units).</summary>
        public Vector2 Offset => OffsetOf(Index);

        public Vector2 OffsetOf(int index) => index <= 0 || Road == null ? Vector2.zero : CentreOf(index) - CentreOf(0);

        // ------------------------------------------------------------------ the wagon's load

        /// <summary>What the wagon would carry: the Store's goods and the materials in stock.</summary>
        public int CargoLoad
        {
            get
            {
                var n = _logistics.System.StoreItems.Count;
                foreach (var kv in _logistics.System.State.SharedMaterials) n += kv.Value;
                return n;
            }
        }

        public int CargoCapacity => _shop.Wagon ? _shop.Definition.WagonCargo(_shop.WagonLevel) : int.MaxValue;

        /// <summary>Leaves the extra materials behind (the biggest stacks first) so the load fits. Returns how many units.</summary>
        public int LeaveExtrasBehind()
        {
            var over = CargoLoad - CargoCapacity;
            var left = 0;
            while (over > 0)
            {
                var biggest = default(KeyValuePair<string, int>);
                foreach (var kv in _logistics.System.State.SharedMaterials) if (kv.Value > biggest.Value) biggest = kv;
                if (biggest.Value <= 0) break;
                var take = Math.Min(over, biggest.Value);
                if (!_logistics.System.TryConsumeShared(new[] { new KeyValuePair<string, int>(biggest.Key, take) }, "left behind: the wagon was full")) break;
                over -= take;
                left += take;
            }
            if (left > 0) _scheduler.SaveNow("left extras behind");
            return left;
        }

        // ------------------------------------------------------------------ build projects (the journey, step 7)

        /// <summary>Gate indices that are build projects (a ravine to bridge, a flood to cross).</summary>
        public IEnumerable<int> Projects()
        {
            if (Road == null) yield break;
            for (var i = 0; i < Road.Gates.Count; i++) if (!string.IsNullOrEmpty(Road.Gates[i].project)) yield return i;
        }

        public string ProjectName(int gate) => Road != null && gate >= 0 && gate < Road.Gates.Count ? Road.Gates[gate].project : "";

        public IReadOnlyList<ResourceCost> ProjectCost(int gate) =>
            Road != null && gate >= 0 && gate < Road.Gates.Count && Road.Gates[gate].projectCost != null ? Road.Gates[gate].projectCost : new List<ResourceCost>();

        public bool IsBuilt(int gate) => Built.Contains(gate);

        public string BuildBlockedReason(int gate)
        {
            if (string.IsNullOrEmpty(ProjectName(gate))) return "Nothing to build here";
            if (IsBuilt(gate)) return "Already built";
            foreach (var c in ProjectCost(gate))
            {
                var have = _logistics.System.SharedAvailable(c.resource.Id);
                if (have < c.quantity) return $"Needs {c.quantity} {c.resource.DisplayName} (you have {have})";
            }
            return null;
        }

        /// <summary>Builds a project with materials from stock: the gap becomes ground (a bridge, a causeway).</summary>
        public string Build(int gate)
        {
            var why = BuildBlockedReason(gate);
            if (why != null) return why;
            var pairs = new List<KeyValuePair<string, int>>();
            foreach (var c in ProjectCost(gate)) pairs.Add(new KeyValuePair<string, int>(c.resource.Id, c.quantity));
            if (!_logistics.System.TryConsumeShared(pairs, $"built: {ProjectName(gate)}")) return "Materials changed; try again";
            Built.Add(gate);
            _scheduler.SaveNow($"built {ProjectName(gate)}");
            ProjectBuilt?.Invoke(gate);
            return null;
        }

        // ------------------------------------------------------------------ moving on

        /// <summary>Whether a world point is in the next camp's clearing.</summary>
        public bool AtNext(Vector2 world)
        {
            if (!HasNext) return false;
            var centre = Road.CampCentre(Index + 1);
            var cx = world.x + 2f * (world.y - 0.25f);
            var cy = 2f * (world.y - 0.25f) - world.x;
            return Vector2.Distance(new Vector2(cx, cy), centre) <= Road.CampRadius(Index + 1) + 0.5f;
        }

        /// <summary>Why the company can't make camp at the next clearing with the supplier standing at <paramref name="world"/> (null: it can).</summary>
        public string MoveOnBlockedReason(Vector2 world)
        {
            if (!HasNext) return "This is the last camp on the road";
            if (!AtNext(world)) return $"Make camp from {NextName}'s clearing";
            // The castle's summons for this camp must be answered first (the journey, step 5).
            if (!_jobs.Board.SummonsAnswered(Index + 1)) return $"Answer the castle's summons first: {_jobs.Board.SummonsFor(Index + 1).Name}";
            var full = new List<string>();
            foreach (var s in _stations.State.Sites.Values)
                if (s.Standing && _stations.PackBlockedReason(s.StationId) != null && _catalogue.TryGet<StationDefinition>(s.StationId, out var d))
                    full.Add(d.DisplayName);
            if (full.Count > 0) return "Collect the goods from the " + string.Join(", ", full) + (full.Count == 1 ? "'s pad first" : " pads first");
            if (CargoLoad > CargoCapacity) return $"The wagon can carry {CargoCapacity}; you have {CargoLoad}. Sell some to the Trader, use some, or leave the extras behind";
            return null;
        }

        /// <summary>Breaks camp and makes camp at the next clearing: every station packs into its kit; the group moves.</summary>
        public string MoveOn(Vector2 world)
        {
            var why = MoveOnBlockedReason(world);
            if (why != null) return why;
            foreach (var s in new List<Domain.Camp.StationSite>(_stations.State.Sites.Values))
                if (s.Standing) _stations.Pack(s.StationId);
            var from = Index;
            Index++;
            _scheduler.SaveNow($"moved on to {Name}");
            Moved?.Invoke(from, Index);
            return null;
        }

        /// <summary>DEBUG/tests: put the company at a camp (as if it had moved there).</summary>
        public void DebugSetIndex(int index)
        {
            var from = Index;
            Index = Road != null ? Math.Max(0, Math.Min(index, Road.CampCount - 1)) : 0;
            _scheduler.SaveNow("debug: camp");
            Moved?.Invoke(from, Index);
        }

        // ------------------------------------------------------------------ save

        void ISaveSection.Write(SaveEnvelope env)
        {
            env.Camp = new CampSaveData { Road = Road?.Id, Index = Index };
            env.Camp.Built.AddRange(Built);
        }

        void ISaveSection.Read(SaveEnvelope env)
        {
            var d = env?.Camp;
            Index = d != null && d.Road == Road?.Id && Road != null ? Math.Max(0, Math.Min(d.Index, Road.CampCount - 1)) : 0;
            Built.Clear();
            if (d != null && d.Road == Road?.Id && d.Built != null) foreach (var b in d.Built) Built.Add(b);
            Reloaded?.Invoke();
        }
    }
}
