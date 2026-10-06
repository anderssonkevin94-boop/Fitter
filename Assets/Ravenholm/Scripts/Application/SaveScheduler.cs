using System;
using System.Collections.Generic;
using Ravenholm.Infrastructure.Save;

namespace Ravenholm.Services
{
    /// <summary>A service that owns one section of the save envelope.</summary>
    public interface ISaveSection
    {
        /// <summary>Copy runtime state into the envelope.</summary>
        void Write(SaveEnvelope envelope);

        /// <summary>Replace runtime state from the envelope, or reset to a fresh profile when null.</summary>
        void Read(SaveEnvelope envelopeOrNull);
    }

    /// <summary>
    /// Single save cadence for all sections: critical commands save immediately, cheap changes
    /// are debounced, lifecycle events flush. Every save is a full snapshot, so ownership is
    /// consistent at any save point.
    /// </summary>
    public sealed class SaveScheduler
    {
        private readonly SaveCoordinator _saves;
        private readonly float _debounceSeconds;
        private readonly List<ISaveSection> _sections = new List<ISaveSection>();
        private bool _dirty;
        private float _secondsSinceSave;

        public SaveScheduler(SaveCoordinator saves, float debounceSeconds)
        {
            _saves = saves;
            _debounceSeconds = debounceSeconds;
        }

        public SaveCoordinator Coordinator => _saves;
        public bool IsDirty => _dirty;

        /// <summary>Raised after every load or save attempt (state may have changed).</summary>
        public event Action Committed;

        public void Register(ISaveSection section) => _sections.Add(section);

        public void LoadAll()
        {
            var envelope = _saves.Load();
            foreach (var s in _sections) s.Read(envelope);
            _dirty = false;
            Committed?.Invoke();
        }

        public void MarkDirty() => _dirty = true;

        public bool SaveNow(string reason)
        {
            var ok = _saves.Save(env => { foreach (var s in _sections) s.Write(env); }, reason);
            if (ok)
            {
                _dirty = false;
                _secondsSinceSave = 0f;
            }
            Committed?.Invoke();
            return ok;
        }

        public void Tick(float unscaledDeltaSeconds)
        {
            _secondsSinceSave += unscaledDeltaSeconds;
            if (_dirty && _secondsSinceSave >= _debounceSeconds) SaveNow("autosave");
        }

        /// <summary>Lifecycle flush (pause, focus loss, quit). Best effort; correctness never depends on it.</summary>
        public void Flush(string reason)
        {
            if (_dirty) SaveNow(reason);
        }

        /// <summary>DEBUG: wipe the profile folder and start every section fresh.</summary>
        public void ResetProfile()
        {
            _saves.Store.DebugDeleteProfile();
            _saves.ResetForFreshProfile();
            foreach (var s in _sections) s.Read(null);
            SaveNow("profile reset");
        }
    }
}
