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
            Jobs = new JobsService(Scheduler, Logistics, Sites, catalogue, balance);
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

        /// <summary>Loads every save section and records the session. Call once after construction.</summary>
        public void Start() => Sandbox.Start();

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
