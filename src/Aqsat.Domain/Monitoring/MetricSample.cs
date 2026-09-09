using Aqsat.Domain.Common;

namespace Aqsat.Domain.Monitoring;

/// <summary>
/// One minute of aggregated request/process telemetry for the platform-owner monitoring dashboard.
/// Platform-level exactly like RiskNetworkSettings: no AgencyId, not RLS-scoped, never exposed to
/// agency users. Written by MetricsSamplerJob from the in-memory RequestMetricsAggregator plus
/// live process/DB probes; pruned after 35 days so the table stays ~50k rows.
/// </summary>
public class MetricSample : SoftDeletableEntity
{
    /// <summary>Start of the minute bucket, UTC, truncated to the minute.</summary>
    public DateTimeOffset MinuteUtc { get; set; }

    public int Requests { get; set; }

    /// <summary>Responses with a 5xx status — the error rate the APM panel and error-rate alert run on.</summary>
    public int Errors { get; set; }

    /// <summary>4xx responses — client errors, kept separate so a scanner hitting 404s never looks like an outage.</summary>
    public int ClientErrors { get; set; }

    public int LatencyP50Ms { get; set; }

    public int LatencyP95Ms { get; set; }

    public int LatencyP99Ms { get; set; }

    /// <summary>API process CPU usage over the minute, 0-100. Null on the first sample after a start
    /// (no delta to divide yet). Process-level only — shared hosting never shows the whole server.</summary>
    public double? CpuPercent { get; set; }

    public double ProcessMemoryMb { get; set; }

    public int ThreadCount { get; set; }

    /// <summary>Free space on the volume the app runs from (GB) — the disk-full alert's input.</summary>
    public double DiskFreeGb { get; set; }

    /// <summary>Roundtrip of "open connection + SELECT 1" in ms — the DB probe. Null when the probe failed.</summary>
    public int? DbProbeMs { get; set; }

    /// <summary>"ok" / "degraded" / "down" — down means the DB probe failed outright.</summary>
    public string HealthStatus { get; set; } = "ok";

    /// <summary>Minutes since the API process started — resets on restart, which is itself signal.</summary>
    public double ProcessUpMinutes { get; set; }
}
