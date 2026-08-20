namespace Aqsat.Application.Concurrency;

/// <summary>Result of an acquire/renew attempt — <see cref="AcquiredByMe"/> distinguishes "I now hold
/// it" from "someone else holds it and it is still active" (docs/CONCURRENCY.md §4).</summary>
public readonly record struct LockStatus(
    bool AcquiredByMe,
    Guid LockId,
    Guid LockedByUserId,
    string LockedByDisplayName,
    DateTimeOffset AcquiredAt,
    DateTimeOffset ExpiresAt);

public readonly record struct ForceReleaseResult(
    Guid LockId, Guid AgencyId, string EntityType, Guid EntityId, Guid PreviousHolderUserId, string PreviousHolderDisplayName);

/// <summary>
/// Layer 3 (docs/CONCURRENCY.md §4) — exclusive write lock, never applied to reads. Acquire/renew
/// must be a single atomic statement (a `MERGE`), not a read-then-write, or two callers can both
/// believe they got the lock.
/// </summary>
public interface ILockService
{
    Task<LockStatus> AcquireOrRenewAsync(
        Guid agencyId, string entityType, Guid entityId, Guid userId, string displayName, TimeSpan duration, CancellationToken ct = default);

    /// <summary>Normal release (save/cancel) — only the current holder can release; releasing
    /// expires the lock immediately instead of waiting out its TTL. No-op if not currently held.</summary>
    Task ReleaseAsync(string entityType, Guid entityId, Guid userId, CancellationToken ct = default);

    Task<LockStatus?> GetActiveAsync(string entityType, Guid entityId, CancellationToken ct = default);

    /// <summary>Returns null if no active lock with this Id exists (already released/expired).</summary>
    Task<ForceReleaseResult?> ForceReleaseAsync(Guid lockId, Guid forcedByUserId, string reason, CancellationToken ct = default);
}
