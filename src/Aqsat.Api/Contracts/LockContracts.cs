namespace Aqsat.Api.Contracts;

public sealed record AcquireLockRequest(string EntityType, Guid EntityId, int DurationMinutes);

public sealed record ReleaseLockRequest(string EntityType, Guid EntityId);

public sealed record ForceReleaseLockRequest(string Reason);

public sealed record LockStatusDto(
    bool AcquiredByMe, Guid LockId, Guid LockedByUserId, string LockedByDisplayName, DateTimeOffset AcquiredAt, DateTimeOffset ExpiresAt);
