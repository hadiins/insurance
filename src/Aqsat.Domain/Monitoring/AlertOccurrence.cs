using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain.Monitoring;

/// <summary>
/// A breach in progress or recently resolved: opened when a rule first fires, extended while it
/// keeps firing (LastSeenAt moves), resolved by the sampler once the condition clears or by the
/// owner from the dashboard. Acknowledged = a human has seen it.
/// </summary>
public class AlertOccurrence : SoftDeletableEntity
{
    public Guid RuleId { get; set; }

    public AlertStatus Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>The metric value observed on the LAST evaluation inside the breach.</summary>
    public double ObservedValue { get; set; }

    /// <summary>Human-readable Persian message composed at write time (rule 31).</summary>
    public string Message { get; set; } = string.Empty;

    public AlertRule Rule { get; set; } = null!;
}
