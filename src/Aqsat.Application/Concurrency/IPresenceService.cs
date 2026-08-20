namespace Aqsat.Application.Concurrency;

public sealed record PresenceEntry(Guid UserId, string UserDisplayName, bool IsEditing, DateTimeOffset LastSeenAt);

public sealed record PresenceRemoval(Guid AgencyId, string EntityType, Guid EntityId);

/// <summary>Layer 2 (docs/CONCURRENCY.md §3) — who has a record open right now. Never gates writes;
/// that is Layer 3's job.</summary>
public interface IPresenceService
{
    Task<IReadOnlyList<PresenceEntry>> UpsertAsync(
        Guid agencyId, string entityType, Guid entityId, Guid userId, string displayName, string connectionId, bool isEditing,
        CancellationToken ct = default);

    Task TouchAsync(string entityType, Guid entityId, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<PresenceEntry>> ListAsync(string entityType, Guid entityId, CancellationToken ct = default);

    /// <summary>Returns the (agency, entity) pairs that lost a viewer, so the caller can re-broadcast
    /// the updated presence list to each affected group.</summary>
    Task<IReadOnlyList<PresenceRemoval>> RemoveByConnectionAsync(string connectionId, CancellationToken ct = default);

    Task<int> SweepExpiredAsync(TimeSpan maxAge, CancellationToken ct = default);
}
