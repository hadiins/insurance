using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain.Monitoring;

/// <summary>
/// A security-relevant occurrence the platform owner's security dashboard runs on: failed logins,
/// rate-limit rejections, permission denials, sensitive setting changes, and detected suspicious
/// patterns. Separate from AuditEntry on purpose — that table is RLS-scoped per agency and
/// describes business actions on records; these events are platform-side and often have no agency
/// at all (a failed login happens before any scope resolves). Written via SecurityEventWriter,
/// never deleted, pruned after 90 days.
/// </summary>
public class SecurityEvent : SoftDeletableEntity
{
    public SecurityEventType Type { get; set; }

    public SecuritySeverity Severity { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Caller IP (from CurrentRequestContext / forwarded headers). Null when unknown.</summary>
    public string? IpAddress { get; set; }

    /// <summary>The mobile someone tried to log in with — even a FAILED attempt's mobile is worth
    /// recording: brute-force detection groups by it, and it is not personal free text (rule 8
    /// concerns prose notes about people, not structured identity fields).</summary>
    public string? Mobile { get; set; }

    /// <summary>Short human-readable Persian summary composed at write time (rule 31).</summary>
    public string Detail { get; set; } = string.Empty;
}
