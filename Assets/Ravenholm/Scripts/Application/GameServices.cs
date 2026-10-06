using System;
using Ravenholm.Content;
using Ravenholm.Domain.Logistics;
using Ravenholm.Domain.Time;
using Ravenholm.Infrastructure.Save;

namespace Ravenholm.Services
{
    /// <summary>
    /// Plain C# service graph built once by the composition root. Later phases add their
    /// domain systems here (one owner per field), not as scene singletons.
    /// </summary>
    public sealed class GameServices
    {
        public GameServices(IClock clock, ContentCatalogue catalogue, BalanceProfile balance, string saveDirectory, string profile,
            string gameBuild, float autosaveDebounceSeconds)
        {
            Clock = clock;
            Catalogue = catalogue;
            Balance = balance;
            ContentReport = catalogue != null ? catalogue.Validate() : NullCatalogueReport();
            Profile = profile;
            Saves = new SaveCoordinator(clock, new SaveStore(saveDirectory), profile, gameBuild, ContentVersionOf(catalogue));
            Scheduler = new SaveScheduler(Saves, autosaveDebounceSeconds);
            Sandbox = new SandboxSession(Scheduler);
            Logistics = new LogisticsService(Scheduler, new CatalogueAcceptanceRules(catalogue), TuningFrom(balance));
            Sites = new SitesService(Scheduler, Logistics, catalogue);
            Production = new ProductionService(Scheduler, Logistics, Sites, catalogue);
            Jobs = new JobsService(Scheduler, Logistics, Sites, Production, catalogue, balance);
            Economy = new EconomyService(Scheduler, catalogue, Jobs, Logistics, Production, Sites);
            Simulation = new SimulationService(Scheduler, Sites, Production, Jobs) { DescribeTrader = Economy.DescribeTrader };
            Tutorial = new TutorialService(Scheduler, catalogue, Logistics, Sites, Production, Jobs, Economy);
            Economy.TraderUsed += Tutorial.NoteTraderUsed;
        }

        public IClock Clock { get; }
        public ContentCatalogue Catalogue { get; }
        public BalanceProfile Balance { get; }
        public ContentValidationReport ContentReport { get; private set; }
        public string Profile { get; }
        public SaveCoordinator Saves { get; }
        public SaveScheduler Scheduler { get; }
        public SandboxSession Sandbox { get; }
        public LogisticsService Logistics { get; }
        public SitesService Sites { get; }
        public ProductionService Production { get; }
        public JobsService Jobs { get; }
        public EconomyService Economy { get; }
        public SimulationService Simulation { get; }
        public TutorialService Tutorial { get; }

        /// <summary>
        /// Boot: load every section, catch up the time away (GDD 15), count the session, then commit
        /// everything with one save. Call once after construction.
        /// </summary>
        public void Start()
        {
            Scheduler.LoadAll();
            var id = $"catchup.{Saves.Status.Generation + 1}";
            var report = Simulation.CatchUp(Saves.LoadedLastAcceptedUtc, Clock.UtcNow, id);
            if (report != null && report.ElapsedMs > 0) Economy.LastCatchUpId = id;
            if (report != null && report.ElapsedMs >= SimulationService.ReportThresholdMs) Simulation.PendingReport = report;
            Sandbox.CountSession();
            Scheduler.SaveNow(report != null && report.ElapsedMs > 0 ? "session start (offline catch-up)" : "session start");
        }

        /// <summary>Returning from the background: catch up the paused interval like a cold start.</summary>
        public AwayReport ResumeFrom(DateTime pausedUtc)
        {
            var id = $"catchup.{Saves.Status.Generation + 1}";
            var report = Simulation.CatchUp(pausedUtc, Clock.UtcNow, id);
            if (report == null || report.ElapsedMs <= 0) return null;
            Economy.LastCatchUpId = id;
            Scheduler.SaveNow("resume (offline catch-up)");
            if (report.ElapsedMs >= SimulationService.ReportThresholdMs) Simulation.PendingReport = report;
            return report;
        }

        /// <summary>
        /// Compact diagnostic report for testers (GDD 24): versions, save status, ownership audit and recent
        /// transactions. Contains no personal data: only game state.
        /// </summary>
        public string DiagnosticReport(string build, int balanceRevision)
        {
            var sb = new System.Text.StringBuilder();
            var st = Saves.Status;
            sb.AppendLine($"Ravenholm Supply Co diagnostic · {Clock.UtcNow:O}");
            sb.AppendLine($"build {build} · schema {Infrastructure.Save.SaveEnvelope.CurrentSchemaVersion} · balance revision {balanceRevision} · content {ContentReport}");
            sb.AppendLine($"save: {st.LoadStatus} · generation {st.Generation} · last saved {st.LastSavedUtc:O} ({st.LastSaveReason}) · error {st.LastError ?? "none"}");
            if (!string.IsNullOrEmpty(st.LoadDetail)) sb.AppendLine("load detail: " + st.LoadDetail);
            if (st.MigratedFromSchema > 0) sb.AppendLine($"migrated from schema {st.MigratedFromSchema}");
            if (st.ClockWentBackwards) sb.AppendLine("device clock went backwards at least once");
            sb.AppendLine(Scheduler.Assisted.Count == 0 ? "debug assistance: none" : "debug assistance used: " + string.Join(", ", Scheduler.Assisted));
            sb.AppendLine($"world seed {Saves.WorldSeed} · last catch-up {Economy.LastCatchUpId}");
            var j = Jobs.State;
            sb.AppendLine($"gold {j.Gold} · reputation {j.Reputation} · expeditions {j.Expeditions.Count} · results {j.Results.Count} · campaign {(Jobs.Board.CampaignComplete ? "complete" : Jobs.Board.CampaignObjective())}");
            var audit = Logistics.System.Audit();
            sb.AppendLine(audit.Count == 0 ? "ownership audit: OK" : "ownership audit FAILED:\n  " + string.Join("\n  ", audit));
            sb.AppendLine("recent transactions:");
            foreach (var t in Logistics.System.History) sb.AppendLine($"  {t.Reason}: {t.Detail}");
            return sb.ToString();
        }

        /// <summary>DEBUG: simulate being away for <paramref name="ms"/> (same path as a real absence).</summary>
        public AwayReport DebugTimeAway(long ms)
        {
            Scheduler.NoteAssisted("simulated time away");
            var id = $"debug-away.{Saves.Status.Generation + 1}";
            var report = Simulation.Simulate(ms, id);
            Economy.LastCatchUpId = id;
            Scheduler.SaveNow("debug: time away");
            Simulation.PendingReport = report;
            return report;
        }

        public ContentValidationReport RevalidateContent()
        {
            ContentReport = Catalogue != null ? Catalogue.Validate() : NullCatalogueReport();
            return ContentReport;
        }

        public static LogisticsTuning TuningFrom(BalanceProfile b) => b == null
            ? new LogisticsTuning()
            : new LogisticsTuning
            {
                Capacity = b.carryCapacity,
                PickupRadius = b.pickupRadius,
                TransferInterval = b.transferInterval,
                OutputPadPriority = b.outputPadPriority,
            };

        /// <summary>Highest per-definition content version; recorded in saves for diagnosis.</summary>
        private static int ContentVersionOf(ContentCatalogue catalogue)
        {
            var v = 0;
            if (catalogue == null) return v;
            foreach (var d in catalogue.All())
                if (d != null && d.ContentVersion > v) v = d.ContentVersion;
            return v;
        }

        private static ContentValidationReport NullCatalogueReport()
        {
            var r = new ContentValidationReport();
            r.Errors.Add("No content catalogue assigned to the boot config");
            return r;
        }
    }
}
