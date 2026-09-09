using Aqsat.Domain.Common;

namespace Aqsat.Domain.Monitoring;

/// <summary>
/// Per-minute per-endpoint traffic rollup (the route TEMPLATE, e.g. /api/policies/{id}, never a
/// concrete id — cardinality stays at ~60 rows no matter the traffic). Written by
/// MetricsSamplerJob from the aggregator's live path counters; pruned after 8 days.
/// </summary>
public class EndpointStat : SoftDeletableEntity
{
    public DateTimeOffset MinuteUtc { get; set; }

    /// <summary>Route template — the APM table groups by this, never by the concrete URL.</summary>
    public string Path { get; set; } = string.Empty;

    public int Count { get; set; }

    public int Errors { get; set; }

    public int TotalMs { get; set; }

    /// <summary>Requests slower than 1s — the "slow endpoint" indicator.</summary>
    public int SlowCount { get; set; }
}
