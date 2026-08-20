using Aqsat.Application.Concurrency;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Concurrency;

public sealed class PresenceService(AppDbContext dbContext, PresenceConnectionRegistry registry) : IPresenceService
{
    public async Task<IReadOnlyList<PresenceEntry>> UpsertAsync(
        Guid agencyId, string entityType, Guid entityId, Guid userId, string displayName, string connectionId, bool isEditing,
        CancellationToken ct = default)
    {
        registry.Track(connectionId, agencyId);

        // Re-stamp immediately before touching an RLS table rather than trusting the caller set it
        // earlier in the call chain — cheap, and removes any doubt about staleness under concurrent
        // Hub invocations sharing this AsyncLocal's ambient flow.
        AgencyContext.Current = agencyId;

        var existing = await dbContext.RecordPresences
            .FirstOrDefaultAsync(p => p.EntityType == entityType && p.EntityId == entityId && p.UserId == userId, ct);

        if (existing is null)
        {
            dbContext.RecordPresences.Add(new RecordPresence
            {
                AgencyId = agencyId,
                EntityType = entityType,
                EntityId = entityId,
                UserId = userId,
                UserDisplayName = displayName,
                ConnectionId = connectionId,
                LastSeenAt = DateTimeOffset.UtcNow,
                IsEditing = isEditing,
            });
        }
        else
        {
            existing.ConnectionId = connectionId;
            existing.UserDisplayName = displayName;
            existing.LastSeenAt = DateTimeOffset.UtcNow;
            existing.IsEditing = isEditing;
        }

        await dbContext.SaveChangesAsync(ct);
        return await ListAsync(entityType, entityId, ct);
    }

    public async Task TouchAsync(string entityType, Guid entityId, Guid userId, CancellationToken ct = default)
    {
        await dbContext.RecordPresences
            .Where(p => p.EntityType == entityType && p.EntityId == entityId && p.UserId == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.LastSeenAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task<IReadOnlyList<PresenceEntry>> ListAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        return await dbContext.RecordPresences
            .AsNoTracking()
            .Where(p => p.EntityType == entityType && p.EntityId == entityId)
            .OrderBy(p => p.LastSeenAt)
            .Select(p => new PresenceEntry(p.UserId, p.UserDisplayName, p.IsEditing, p.LastSeenAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PresenceRemoval>> RemoveByConnectionAsync(string connectionId, CancellationToken ct = default)
    {
        if (!registry.TryGetAgency(connectionId, out var agencyId))
        {
            // Registry entry already gone (e.g. process restart) — nothing we can clean up without
            // knowing the agency, and RLS means we can't discover it any other way.
            return [];
        }

        registry.Forget(connectionId);
        AgencyContext.Current = agencyId;

        var rows = await dbContext.RecordPresences.Where(p => p.ConnectionId == connectionId).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return [];
        }

        var removed = rows.Select(r => new PresenceRemoval(agencyId, r.EntityType, r.EntityId)).ToList();
        dbContext.RecordPresences.RemoveRange(rows);
        await dbContext.SaveChangesAsync(ct);
        return removed;
    }

    public async Task<int> SweepExpiredAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        return await dbContext.RecordPresences.Where(p => p.LastSeenAt < cutoff).ExecuteDeleteAsync(ct);
    }
}
