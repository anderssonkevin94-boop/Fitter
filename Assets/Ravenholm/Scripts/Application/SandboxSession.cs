using System;
using Ravenholm.Domain.Sandbox;
using Ravenholm.Infrastructure.Save;

namespace Ravenholm.Services
{
    /// <summary>
    /// PHASE 0 FIXTURE service for the debug sandbox. Demonstrates the intended pattern:
    /// presentation submits commands, this service owns the state, commits a save for
    /// "critical" changes, debounces cheap ones, then raises typed events.
    /// </summary>
    public sealed class SandboxSession
    {
        private readonly SaveCoordinator _saves;
        private readonly float _autosaveDebounceSeconds;
        private SandboxState _state = new SandboxState();
        private bool _dirty;
        private float _secondsSinceSave;

        public SandboxSession(SaveCoordinator saves, float autosaveDebounceSeconds)
        {
            _saves = saves;
            _autosaveDebounceSeconds = autosaveDebounceSeconds;
        }

        /// <summary>Read-only copy for presentation.</summary>
        public SandboxState State => _state.Clone();

        public event Action<SandboxState> StateChanged;

        /// <summary>Loads (or creates) the profile and counts this session.</summary>
        public void Start()
        {
            LoadFromDisk();
            _state.SessionCount++;
            Commit("session start");
        }

        public void LoadFromDisk()
        {
            var envelope = _saves.Load();
            _state = envelope != null ? FromSave(envelope.Sandbox) : new SandboxState();
            _dirty = false;
            StateChanged?.Invoke(State);
        }

        /// <summary>Sample "critical transaction": saved immediately.</summary>
        public void RingBell()
        {
            _state.BellRingCount++;
            Commit("bell rung");
        }

        /// <summary>Cheap, frequent change: saved on a debounce.</summary>
        public void ReportPlayerPosition(float x, float y)
        {
            if (Math.Abs(x - _state.PlayerX) < 0.01f && Math.Abs(y - _state.PlayerY) < 0.01f) return;
            _state.PlayerX = x;
            _state.PlayerY = y;
            _dirty = true;
        }

        public void Tick(float unscaledDeltaSeconds)
        {
            _secondsSinceSave += unscaledDeltaSeconds;
            if (_dirty && _secondsSinceSave >= _autosaveDebounceSeconds) Commit("autosave");
        }

        /// <summary>Lifecycle flush (pause, focus loss, quit). Best effort; correctness never depends on it.</summary>
        public void Flush(string reason)
        {
            if (_dirty) Commit(reason);
        }

        public bool SaveNow(string reason) => Commit(reason);

        /// <summary>DEBUG FIXTURE: wipes this profile's files and starts fresh. UI must confirm first.</summary>
        public void ResetProfile()
        {
            _saves.Store.DebugDeleteProfile();
            _saves.ResetForFreshProfile();
            _state = new SandboxState();
            Commit("profile reset");
        }

        private bool Commit(string reason)
        {
            var ok = _saves.Save(env => env.Sandbox = ToSave(_state), reason);
            if (ok)
            {
                _dirty = false;
                _secondsSinceSave = 0f;
            }
            StateChanged?.Invoke(State);
            return ok;
        }

        private static SandboxState FromSave(SandboxSaveData d) => new SandboxState
        {
            BellRingCount = d?.BellRingCount ?? 0,
            SessionCount = d?.SessionCount ?? 0,
            PlayerX = d?.PlayerX ?? 0f,
            PlayerY = d?.PlayerY ?? 0f,
        };

        private static SandboxSaveData ToSave(SandboxState s) => new SandboxSaveData
        {
            BellRingCount = s.BellRingCount,
            SessionCount = s.SessionCount,
            PlayerX = s.PlayerX,
            PlayerY = s.PlayerY,
        };
    }
}
