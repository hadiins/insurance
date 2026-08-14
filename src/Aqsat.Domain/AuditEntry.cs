using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// One central immutable append-only audit table — per-record history is a query against this,
/// not a second table. Never updated after insert: no soft-delete, no rowversion. PolicyId is
/// always populated, even when the subject is an installment or a cheque, so per-file history is a
/// single indexed lookup (IX_Audit_Policy on (AgencyId, PolicyId, OccurredAt DESC)).
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }
    public Guid AgencyId { get; set; }

    public Guid UserId { get; set; }
    public string UserDisplayName { get; set; } = default!;

    public string EntityType { get; set; } = default!;
    public Guid EntityId { get; set; }
    public Guid PolicyId { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>Composed at write time, not render time.</summary>
    public string Description { get; set; } = default!;

    /// <summary>Never for encrypted fields — record "changed", not from-what-to-what.</summary>
    public string? ChangesJson { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public string? IpAddress { get; set; }
}
