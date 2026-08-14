namespace Aqsat.Domain.Common;

/// <summary>
/// Soft delete + optimistic concurrency (rowversion — concurrency layer 1 per
/// docs/CONCURRENCY.md). No hard deletes anywhere in the system. Applies to every entity,
/// including the identity/hierarchy tables that don't carry AgencyId.
/// </summary>
public abstract class SoftDeletableEntity : Entity
{
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public byte[] RowVersion { get; set; } = default!;
}
