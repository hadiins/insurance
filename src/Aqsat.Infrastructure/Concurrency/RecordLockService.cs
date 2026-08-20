using Aqsat.Application.Concurrency;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Concurrency;

/// <summary>
/// docs/CONCURRENCY.md §4. RecordLockConfiguration's unique index only filters on
/// `ForceReleasedAt IS NULL` (SQL Server rejects SYSDATETIMEOFFESET() in a filtered-index
/// predicate), so — unlike the doc's own MERGE example — an expired-but-not-force-released row
/// still occupies that unique slot. The MERGE below accounts for that: it matches on
/// (EntityType, EntityId, ForceReleasedAt IS NULL) alone and reassigns the existing row to the new
/// caller whenever it is expired or already theirs, rather than trying to INSERT a second row.
/// </summary>
public sealed class RecordLockService(AppDbContext dbContext) : ILockService
{
    public async Task<LockStatus> AcquireOrRenewAsync(
        Guid agencyId, string entityType, Guid entityId, Guid userId, string displayName, TimeSpan duration, CancellationToken ct = default)
    {
        var minutes = (int)Math.Ceiling(duration.TotalMinutes);
        var newId = Guid.NewGuid();

        const string sql = """
            MERGE dbo.RecordLocks WITH (HOLDLOCK) AS t
            USING (SELECT @EntityType AS EntityType, @EntityId AS EntityId) AS s
               ON t.EntityType = s.EntityType AND t.EntityId = s.EntityId AND t.ForceReleasedAt IS NULL
            WHEN MATCHED AND (t.ExpiresAt <= SYSDATETIMEOFFSET() OR t.LockedByUserId = @UserId) THEN
                UPDATE SET
                    LockedByUserId = @UserId,
                    LockedByDisplayName = @DisplayName,
                    AcquiredAt = CASE
                        WHEN t.LockedByUserId = @UserId AND t.ExpiresAt > SYSDATETIMEOFFSET() THEN t.AcquiredAt
                        ELSE SYSDATETIMEOFFSET()
                    END,
                    ExpiresAt = DATEADD(MINUTE, @Minutes, SYSDATETIMEOFFSET()),
                    LastRenewedAt = SYSDATETIMEOFFSET()
            WHEN NOT MATCHED THEN
                INSERT (Id, AgencyId, EntityType, EntityId, LockedByUserId, LockedByDisplayName, AcquiredAt, ExpiresAt, LastRenewedAt)
                VALUES (@NewId, @AgencyId, @EntityType, @EntityId, @UserId, @DisplayName, SYSDATETIMEOFFSET(),
                        DATEADD(MINUTE, @Minutes, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET())
            OUTPUT inserted.Id, inserted.LockedByUserId, inserted.LockedByDisplayName, inserted.AcquiredAt, inserted.ExpiresAt;
            """;

        LockStatus? acquired = null;

        await dbContext.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqlConnection)dbContext.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameter(command, "@EntityType", entityType);
            AddParameter(command, "@EntityId", entityId);
            AddParameter(command, "@UserId", userId);
            AddParameter(command, "@DisplayName", displayName);
            AddParameter(command, "@Minutes", minutes);
            AddParameter(command, "@NewId", newId);
            AddParameter(command, "@AgencyId", agencyId);

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                acquired = new LockStatus(
                    AcquiredByMe: true,
                    LockId: reader.GetGuid(0),
                    LockedByUserId: reader.GetGuid(1),
                    LockedByDisplayName: reader.GetString(2),
                    AcquiredAt: reader.GetFieldValue<DateTimeOffset>(3),
                    ExpiresAt: reader.GetFieldValue<DateTimeOffset>(4));
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }

        if (acquired is not null)
        {
            return acquired.Value;
        }

        // Matched, but held by someone else and still active — no OUTPUT row was produced.
        var current = await GetActiveAsync(entityType, entityId, ct);
        if (current is null)
        {
            // Shouldn't happen (a non-outputting MATCH implies an active row exists), but fail
            // loudly rather than silently pretend we acquired it (CLAUDE.md rule 15).
            throw new InvalidOperationException($"Lock acquire for {entityType}/{entityId} matched but no active lock was found afterward.");
        }

        return current.Value with { AcquiredByMe = false };
    }

    public async Task ReleaseAsync(string entityType, Guid entityId, Guid userId, CancellationToken ct = default)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE dbo.RecordLocks SET ExpiresAt = SYSDATETIMEOFFSET()
            WHERE EntityType = {entityType} AND EntityId = {entityId} AND LockedByUserId = {userId} AND ForceReleasedAt IS NULL
            """,
            ct);
    }

    public async Task<LockStatus?> GetActiveAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        var active = await dbContext.RecordLocks
            .AsNoTracking()
            .Where(l => l.EntityType == entityType && l.EntityId == entityId
                && l.ForceReleasedAt == null && l.ExpiresAt > DateTimeOffset.UtcNow)
            .FirstOrDefaultAsync(ct);

        return active is null
            ? null
            : new LockStatus(false, active.Id, active.LockedByUserId, active.LockedByDisplayName, active.AcquiredAt, active.ExpiresAt);
    }

    public async Task<ForceReleaseResult?> ForceReleaseAsync(Guid lockId, Guid forcedByUserId, string reason, CancellationToken ct = default)
    {
        var recordLock = await dbContext.RecordLocks
            .FirstOrDefaultAsync(l => l.Id == lockId && l.ForceReleasedAt == null && l.ExpiresAt > DateTimeOffset.UtcNow, ct);
        if (recordLock is null)
        {
            return null;
        }

        var previousHolderUserId = recordLock.LockedByUserId;
        var previousHolderDisplayName = recordLock.LockedByDisplayName;

        recordLock.ForceReleasedByUserId = forcedByUserId;
        recordLock.ForceReleasedAt = DateTimeOffset.UtcNow;
        recordLock.ForceReleaseReason = reason;
        await dbContext.SaveChangesAsync(ct);

        return new ForceReleaseResult(
            recordLock.Id, recordLock.AgencyId, recordLock.EntityType, recordLock.EntityId, previousHolderUserId, previousHolderDisplayName);
    }

    private static void AddParameter(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}
