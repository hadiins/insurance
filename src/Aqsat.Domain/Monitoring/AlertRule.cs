using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain.Monitoring;

/// <summary>
/// One owner-defined threshold rule ("P95 latency above 3000ms for 10 minutes"). Evaluated every
/// minute by MetricsSamplerJob against the MetricSample/SecurityEvent windows; a breach opens (or
/// extends) an AlertOccurrence. Seeded with sensible defaults on startup, fully editable from
/// «هشدارها و قوانین».
/// </summary>
public class AlertRule : SoftDeletableEntity
{
    /// <summary>Persian display name, e.g. «نرخ خطای بالا».</summary>
    public string Name { get; set; } = string.Empty;

    public AlertMetric Metric { get; set; }

    public AlertComparator Comparator { get; set; }

    public double Threshold { get; set; }

    /// <summary>How many recent minutes the rule looks at (aggregated or max, per metric).</summary>
    public int WindowMinutes { get; set; }

    public SecuritySeverity Severity { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Critical rules can page the owner by SMS — opted in per rule because every send
    /// costs real money (rule 25's cost discipline applies to owner alerts too).</summary>
    public bool SmsNotify { get; set; }

    /// <summary>Last SMS sent for this rule — enforces the 30-minute cooldown so a flapping
    /// threshold can't drain the SMS balance.</summary>
    public DateTimeOffset? LastNotifiedAt { get; set; }
}
