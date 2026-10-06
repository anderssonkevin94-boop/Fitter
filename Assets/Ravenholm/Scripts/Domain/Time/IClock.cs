using System;

namespace Ravenholm.Domain.Time
{
    /// <summary>Wall-clock source. Inject this instead of reading DateTime.UtcNow directly.</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    /// <summary>Test and debug clock that only moves when told to.</summary>
    public sealed class ManualClock : IClock
    {
        public ManualClock(DateTime startUtc) { UtcNow = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc); }

        public DateTime UtcNow { get; private set; }

        public void Set(DateTime utc) { UtcNow = DateTime.SpecifyKind(utc, DateTimeKind.Utc); }

        public void Advance(TimeSpan delta) { UtcNow = UtcNow + delta; }
    }

    /// <summary>
    /// Keeps the "last accepted" timestamp monotonic. A device clock moved backwards never
    /// rewinds saved time (GDD 18: clamp negative elapsed to zero, keep the maximum accepted).
    /// </summary>
    public static class ClockGuard
    {
        public static DateTime Accept(DateTime lastAcceptedUtc, DateTime nowUtc, out bool wentBackwards)
        {
            wentBackwards = nowUtc < lastAcceptedUtc;
            return wentBackwards ? lastAcceptedUtc : nowUtc;
        }
    }
}
