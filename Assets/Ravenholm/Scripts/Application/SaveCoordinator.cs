using System;
using Ravenholm.Domain.Time;
using Ravenholm.Infrastructure.Save;

namespace Ravenholm.Services
{
    public sealed class SaveStatus
    {
        public SaveLoadStatus LoadStatus;
        public string LoadDetail = "";
        public int MigratedFromSchema;
        public long Generation;
        public DateTime? LastSavedUtc;
        public string LastSaveReason = "";
        public string LastError;
        public bool ClockWentBackwards;
        public string Directory = "";
    }

    /// <summary>
    /// Owns the save envelope lifecycle for one profile: load/migrate/recover, generation
    /// numbering and monotonic accepted time. Domain services hand it state; it never
    /// decides game rules.
    /// </summary>
    public sealed class SaveCoordinator
    {
        private readonly IClock _clock;
        private readonly SaveStore _store;
        private readonly string _profile;
        private readonly string _gameBuild;
        private readonly int _contentVersion;
        private DateTime _lastAcceptedUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

        public SaveCoordinator(IClock clock, SaveStore store, string profile, string gameBuild, int contentVersion)
        {
            _clock = clock;
            _store = store;
            _profile = profile;
            _gameBuild = gameBuild;
            _contentVersion = contentVersion;
            Status.Directory = store.Directory;
        }

        public SaveStatus Status { get; } = new SaveStatus();

        /// <summary>Last accepted UTC of the save that was just loaded (null for a fresh profile). Basis of offline catch-up.</summary>
        public DateTime? LoadedLastAcceptedUtc { get; private set; }

        public IClock Clock => _clock;
        public ulong WorldSeed { get; private set; }
        public SaveStore Store => _store;

        public event Action<SaveStatus> StatusChanged;

        /// <summary>Loads the profile. Returns the envelope, or null when a fresh profile should start.</summary>
        public SaveEnvelope Load()
        {
            SaveEnvelope envelope = null;
            var migratedFrom = 0;
            var result = _store.Load(json =>
            {
                if (!SaveSerializer.TryFromJson(json, out var env, out var from, out var err)) return err;
                if (env.Profile != _profile) return $"profile mismatch ('{env.Profile}' in a '{_profile}' folder)";
                envelope = env;
                migratedFrom = from;
                return null;
            });

            Status.LoadStatus = result.Status;
            Status.LoadDetail = string.Join("; ", result.Problems);
            if (result.QuarantinedFiles.Count > 0) Status.LoadDetail += $" | kept aside: {string.Join(", ", result.QuarantinedFiles)}";
            Status.MigratedFromSchema = migratedFrom != SaveEnvelope.CurrentSchemaVersion ? migratedFrom : 0;

            if (envelope != null && (result.Status == SaveLoadStatus.LoadedPrimary || result.Status == SaveLoadStatus.RecoveredFromBackup))
            {
                WorldSeed = envelope.GetWorldSeed();
                Status.Generation = envelope.SaveGeneration;
                _lastAcceptedUtc = envelope.LastAcceptedUtc;
                LoadedLastAcceptedUtc = envelope.LastAcceptedUtc;
                _store.NoteLoadedGeneration(envelope.SaveGeneration);
            }
            else
            {
                envelope = null;
                LoadedLastAcceptedUtc = null;
                WorldSeed = NewWorldSeed();
            }

            StatusChanged?.Invoke(Status);
            return envelope;
        }

        /// <summary>Writes a new generation. <paramref name="fill"/> copies domain state into the envelope.</summary>
        public bool Save(Action<SaveEnvelope> fill, string reason)
        {
            _lastAcceptedUtc = ClockGuard.Accept(_lastAcceptedUtc, _clock.UtcNow, out var backwards);
            Status.ClockWentBackwards |= backwards;

            var envelope = new SaveEnvelope
            {
                GameBuild = _gameBuild,
                ContentVersion = _contentVersion,
                Profile = _profile,
                SaveGeneration = Status.Generation + 1,
                LastAcceptedUtc = _lastAcceptedUtc,
            };
            envelope.SetWorldSeed(WorldSeed);
            fill(envelope);

            var ok = _store.Write(SaveSerializer.ToJson(envelope), envelope.SaveGeneration, out var error);
            if (ok)
            {
                Status.Generation = envelope.SaveGeneration;
                Status.LastSavedUtc = _lastAcceptedUtc;
                Status.LastSaveReason = reason;
                Status.LastError = null;
            }
            else
            {
                Status.LastError = error;
            }
            StatusChanged?.Invoke(Status);
            return ok;
        }

        /// <summary>DEBUG: forget in-memory generation after the profile folder was wiped.</summary>
        public void ResetForFreshProfile()
        {
            Status.Generation = 0;
            Status.LastSavedUtc = null;
            Status.LastError = null;
            _lastAcceptedUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
            WorldSeed = NewWorldSeed();
        }

        private ulong NewWorldSeed()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            return BitConverter.ToUInt64(bytes, 0) ^ (ulong)_clock.UtcNow.Ticks;
        }
    }
}
