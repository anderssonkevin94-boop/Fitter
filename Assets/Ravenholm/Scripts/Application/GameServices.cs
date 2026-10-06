using Ravenholm.Content;
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
        public GameServices(IClock clock, ContentCatalogue catalogue, string saveDirectory, string profile,
            string gameBuild, float autosaveDebounceSeconds)
        {
            Clock = clock;
            Catalogue = catalogue;
            ContentReport = catalogue != null ? catalogue.Validate() : NullCatalogueReport();
            Profile = profile;
            Saves = new SaveCoordinator(clock, new SaveStore(saveDirectory), profile, gameBuild, ContentVersionOf(catalogue));
            Sandbox = new SandboxSession(Saves, autosaveDebounceSeconds);
        }

        public IClock Clock { get; }
        public ContentCatalogue Catalogue { get; }
        public ContentValidationReport ContentReport { get; private set; }
        public string Profile { get; }
        public SaveCoordinator Saves { get; }
        public SandboxSession Sandbox { get; }

        public ContentValidationReport RevalidateContent()
        {
            ContentReport = Catalogue != null ? Catalogue.Validate() : NullCatalogueReport();
            return ContentReport;
        }

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
