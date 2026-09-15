using System.Collections.Concurrent;

namespace Aqsat.Api;

/// <summary>
/// One-record-per-minute throttle for 429 SecurityEvents. A request flood blocked by the rate
/// limiter would otherwise write one database row per rejected request — turning a cheap
/// short-circuit into a write amplifier the attacker fully controls. The security dashboard
/// counts occurrences per (policy, IP) anyway; the first event per minute is the signal, the
/// other 299 are the same signal again.
/// </summary>
internal static class SecurityEventThrottle
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastRecorded = new();

    internal static bool ShouldRecord(string key)
    {
        var now = DateTimeOffset.UtcNow;

        // Cap growth from spoofed/rotating keys: shed expired entries past 10k. Losing a throttle
        // slot only means an extra event row, never a lost one.
        if (LastRecorded.Count > 10_000)
        {
            foreach (var stale in LastRecorded.Where(kv => now - kv.Value >= Interval))
            {
                LastRecorded.TryRemove(stale.Key, out _);
            }
        }

        var last = LastRecorded.GetOrAdd(key, _ => DateTimeOffset.MinValue);
        while (true)
        {
            if (now - last < Interval) return false;
            if (LastRecorded.TryUpdate(key, now, last)) return true;
            if (!LastRecorded.TryGetValue(key, out last)) return false;
        }
    }
}
