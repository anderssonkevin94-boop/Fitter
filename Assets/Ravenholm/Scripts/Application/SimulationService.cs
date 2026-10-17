using System;
using System.Collections.Generic;
using Ravenholm.Domain.Jobs;
using Ravenholm.Domain.Production;
using Ravenholm.Domain.Sites;

namespace Ravenholm.Services
{
    /// <summary>What happened while the player was away (GDD 15/16 return summary).</summary>
    public sealed class AwayReport
    {
        public string CatchUpId = "";
        public long ElapsedMs;
        public long ResourceMs;
        public bool Capped;
        public bool ClockWentBackwards;
        /// <summary>Site ID → raw units deposited at its staging pile.</summary>
        public readonly SortedDictionary<string, int> Deposits = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Site ID → why it stopped (waiting for a decision, staging full), if it did.</summary>
        public readonly SortedDictionary<string, string> SiteBlockers = new SortedDictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Resource ID → processed units made.</summary>
        public readonly SortedDictionary<string, int> Processed = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly List<ResultRecord> Results = new List<ResultRecord>();
        public readonly List<string> Recovered = new List<string>();
        public string TraderStatus = "";

        public bool IsEmpty => Deposits.Count == 0 && Processed.Count == 0 && Results.Count == 0 && Recovered.Count == 0 && SiteBlockers.Count == 0;
    }

    /// <summary>
    /// The one simulation clock. Online play and offline catch-up both advance sites, processing and
    /// expeditions through <see cref="Advance"/>, stepping exactly to each quest completion so the
    /// same interval gives the same result however it is split. Offline gathering and processing are
    /// capped (GDD 15: 8 hours); expeditions and the Trader schedule see the whole absence.
    /// Catch-up never makes player decisions: no hauling, no depth choice, no purchases.
    /// </summary>
    public sealed class SimulationService
    {
        public const long OfflineResourceCapMs = 8L * 60 * 60 * 1000;
        /// <summary>Absences shorter than this get no summary panel.</summary>
        public const long ReportThresholdMs = 60 * 1000;

        private readonly SaveScheduler _scheduler;
        private readonly SitesService _sites;
        private readonly ProductionService _production;
        private readonly JobsService _jobs;
        private double _msRemainder;

        public SimulationService(SaveScheduler scheduler, SitesService sites, ProductionService production, JobsService jobs)
        {
            _scheduler = scheduler;
            _sites = sites;
            _production = production;
            _jobs = jobs;
        }

        /// <summary>Set by the economy layer to describe the Trader in away summaries.</summary>
        public Func<string> DescribeTrader;

        /// <summary>The summary of the latest catch-up worth showing (cleared once shown).</summary>
        public AwayReport PendingReport
        {
            get => _pending;
            set { _pending = value; if (value != null) LastReport = value; }
        }

        private AwayReport _pending;

        /// <summary>The latest away summary this session, kept after dismissal so it can be reopened (GDD 18).</summary>
        public AwayReport LastReport { get; private set; }

        /// <summary>Online frame tick. Shares the debug speed multiplier with everything.</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0) return;
            var exact = deltaSeconds * 1000.0 * _sites.SpeedMultiplier + _msRemainder;
            var whole = (long)Math.Floor(exact);
            _msRemainder = exact - whole;
            if (whole <= 0) return;
            Advance(whole, whole);
            _scheduler.MarkDrifted();   // time passing: saved every 20 s at most
        }

        /// <summary>
        /// Advances <paramref name="totalMs"/> of world time; gathering and processing run only for the
        /// first <paramref name="resourceMs"/> of it. Steps land exactly on each quest completion, so
        /// processing and sites reach that moment before the expedition returns.
        /// </summary>
        public void Advance(long totalMs, long resourceMs)
        {
            var remaining = Math.Max(0, totalMs);
            var resourceLeft = Math.Max(0, Math.Min(resourceMs, remaining));
            while (remaining > 0)
            {
                // Land exactly on each quest completion and each of the castle's job offers.
                var next = _jobs.Board != null ? Math.Min(_jobs.Board.NextCompletionDelay(), _jobs.Board.NextCastleOfferDelay()) : long.MaxValue;
                var step = Math.Min(remaining, Math.Max(1, next));
                var rstep = Math.Min(step, resourceLeft);
                if (rstep > 0)
                {
                    _sites.AdvanceMs(rstep);
                    _production.AdvanceMs(rstep);
                    resourceLeft -= rstep;
                }
                if (_jobs.Board != null) _jobs.AdvanceMs(step);
                remaining -= step;
            }
        }

        /// <summary>
        /// Offline catch-up from the loaded save's last accepted time to now. Changes nothing for a
        /// fresh profile or a zero/backwards interval. The caller commits the result with one save.
        /// </summary>
        public AwayReport CatchUp(DateTime? lastAcceptedUtc, DateTime nowUtc, string catchUpId)
        {
            if (lastAcceptedUtc == null) return null;
            var elapsed = (long)Math.Floor((nowUtc - lastAcceptedUtc.Value).TotalMilliseconds);
            var report = Simulate(Math.Max(0, elapsed), catchUpId);
            report.ClockWentBackwards = elapsed < 0;
            return report;
        }

        /// <summary>
        /// Profiling helper (tests and the device probe): a busy company for catch-up timing. Facilities hold
        /// enough delivered raw stock to run the whole 8-hour cap, and every crew recruit is hired.
        /// </summary>
        public static void MakeBusyForProfiling(GameServices g)
        {
            foreach (var id in new[] { "resource.iron_ore", "resource.copper_ore", "resource.coal", "resource.cedar_log", "resource.maple_log" })
                g.Logistics.System.ProduceShared(id, 20000, "profiling stock");
            foreach (var site in g.Catalogue.Sites)
                foreach (var w in site.Recruits) g.Sites.AddWorker(site.Id, w);
            foreach (var st in g.Catalogue.Stations) g.Production.System.SetLevel(st.Id, st.MaxLevel);
        }

        /// <summary>Simulates an absence of <paramref name="elapsedMs"/> (also the debug "time away" button).</summary>
        public AwayReport Simulate(long elapsedMs, string catchUpId)
        {
            var report = new AwayReport { CatchUpId = catchUpId, ElapsedMs = Math.Max(0, elapsedMs) };
            report.ResourceMs = Math.Min(report.ElapsedMs, OfflineResourceCapMs);
            report.Capped = report.ElapsedMs > OfflineResourceCapMs;
            if (report.ElapsedMs == 0) return report;

            var injuredBefore = new List<string>();
            if (_jobs.Board != null)
                foreach (var c in _jobs.State.Contractors.Values)
                    if (c.Status == ContractorStatus.Injured) injuredBefore.Add(c.Id);

            var handlers = new List<KeyValuePair<SiteSimulator, Action<string, int>>>();
            foreach (var sim in _sites.Sites)
            {
                var siteId = sim.Config.SiteId;
                Action<string, int> h = (worker, q) =>
                {
                    report.Deposits.TryGetValue(siteId, out var n);
                    report.Deposits[siteId] = n + q;
                };
                sim.Deposited += h;
                handlers.Add(new KeyValuePair<SiteSimulator, Action<string, int>>(sim, h));
            }
            Action<string, ProcessRecipe> onBatch = (facility, r) =>
            {
                report.Processed.TryGetValue(r.Output, out var n);
                report.Processed[r.Output] = n + r.OutputQuantity;
            };
            Action<ResultRecord> onResult = r => report.Results.Add(r);
            _production.System.BatchCompleted += onBatch;
            _jobs.Completed += onResult;
            _jobs.DeferCompletionSaves = true;
            try
            {
                Advance(report.ElapsedMs, report.ResourceMs);
            }
            finally
            {
                _jobs.DeferCompletionSaves = false;
                foreach (var kv in handlers) kv.Key.Deposited -= kv.Value;
                _production.System.BatchCompleted -= onBatch;
                _jobs.Completed -= onResult;
            }

            foreach (var sim in _sites.Sites)
            {
                if (sim.Record.Status == SiteStatus.AwaitDecision)
                    report.SiteBlockers[sim.Config.SiteId] = sim.DigMode ? "Your tunnel was blocked: dig a new one" : "Waiting for you to choose: continue or go deeper";
                else
                    foreach (var w in sim.Record.Workers)
                        if (w.State == WorkerState.StagingFull) { report.SiteBlockers[sim.Config.SiteId] = "Staging is full: haul it to the facilities"; break; }
            }
            if (_jobs.Board != null)
                foreach (var id in injuredBefore)
                    if (_jobs.State.Contractors.TryGetValue(id, out var c) && c.Status != ContractorStatus.Injured) report.Recovered.Add(c.Name);
            report.TraderStatus = DescribeTrader?.Invoke() ?? "";
            return report;
        }
    }
}
