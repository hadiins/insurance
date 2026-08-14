namespace Aqsat.Domain;

/// <summary>
/// Concurrency layer 3 (docs/CONCURRENCY.md) — exclusive write lock. Only one active lock per
/// record (enforced by a filtered unique index, not application logic alone).
/// </summary>
public class RecordLock
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }

    public string EntityType { get; set; } = default!;
    public Guid EntityId { get; set; }

    public Guid LockedByUserId { get; set; }
    public string LockedByDisplayName { get; set; } = default!;

    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastRenewedAt { get; set; }

    public Guid? ForceReleasedByUserId { get; set; }
    public DateTimeOffset? ForceReleasedAt { get; set; }
    public string? ForceReleaseReason { get; set; }
}
