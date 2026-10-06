using System;
using Ravenholm.Domain.Sandbox;
using Ravenholm.Infrastructure.Save;

namespace Ravenholm.Services
{
    /// <summary>
    /// PHASE 0 FIXTURE service for the debug sandbox: the bell counter, session count and
    /// player position. Owns the "sandbox" save section.
    /// </summary>
    public sealed class SandboxSession : ISaveSection
    {
        private readonly SaveScheduler _scheduler;
        private SandboxState _state = new SandboxState();

        public SandboxSession(SaveScheduler scheduler)
        {
            _scheduler = scheduler;
            scheduler.Register(this);
            scheduler.Committed += () => StateChanged?.Invoke(State);
        }

        /// <summary>Read-only copy for presentation.</summary>
        public SandboxState State => _state.Clone();

        public event Action<SandboxState> StateChanged;

        /// <summary>Loads every registered section, then counts this session. Call once at boot.</summary>
        public void Start()
        {
            _scheduler.LoadAll();
            _state.SessionCount++;
            _scheduler.SaveNow("session start");
        }

        /// <summary>Counts a session without loading (the boot sequence loads and catches up first).</summary>
        public void CountSession() => _state.SessionCount++;

        /// <summary>DEBUG: reload every section from disk.</summary>
        public void LoadFromDisk() => _scheduler.LoadAll();

        /// <summary>Sample "critical transaction": saved immediately.</summary>
        public void RingBell()
        {
            _state.BellRingCount++;
            _scheduler.SaveNow("bell rung");
        }

        /// <summary>Cheap, frequent change: saved on the debounce.</summary>
        public void ReportPlayerPosition(float x, float y)
        {
            if (Math.Abs(x - _state.PlayerX) < 0.01f && Math.Abs(y - _state.PlayerY) < 0.01f) return;
            _state.PlayerX = x;
            _state.PlayerY = y;
            _scheduler.MarkDirty();
        }

        public void Tick(float unscaledDeltaSeconds) => _scheduler.Tick(unscaledDeltaSeconds);
        public void Flush(string reason) => _scheduler.Flush(reason);
        public bool SaveNow(string reason) => _scheduler.SaveNow(reason);
        public void ResetProfile() => _scheduler.ResetProfile();

        void ISaveSection.Write(SaveEnvelope env)
        {
            env.Sandbox = new SandboxSaveData
            {
                BellRingCount = _state.BellRingCount,
                SessionCount = _state.SessionCount,
                PlayerX = _state.PlayerX,
                PlayerY = _state.PlayerY,
            };
            env.Sandbox.Assisted.AddRange(_scheduler.Assisted);
        }

        void ISaveSection.Read(SaveEnvelope env)
        {
            var d = env?.Sandbox;
            _scheduler.Assisted.Clear();
            if (d?.Assisted != null) foreach (var a in d.Assisted) _scheduler.Assisted.Add(a);
            _state = new SandboxState
            {
                BellRingCount = d?.BellRingCount ?? 0,
                SessionCount = d?.SessionCount ?? 0,
                PlayerX = d?.PlayerX ?? 0f,
                PlayerY = d?.PlayerY ?? 0f,
            };
        }
    }
}
