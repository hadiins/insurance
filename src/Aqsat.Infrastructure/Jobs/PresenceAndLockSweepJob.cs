using Aqsat.Application.Concurrency;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Jobs;

/// <summary>
/// docs/CONCURRENCY.md §3/§4 checklist: sweep stale presence rows (no heartbeat within the expiry
/// window — the backstop for a client that vanished without a clean SignalR disconnect, e.g. a
/// crashed tab) and expired lock rows (pure housekeeping — MERGE-based acquire already reassigns an
/// expired-but-present row correctly either way) every 2 minutes, per agency since both tables are
/// RLS-scoped.
/// </summary>
public sealed class PresenceAndLockSweepJob(AppDbContext dbContext, IPresenceService presenceService)
{
    public static readonly TimeSpan PresenceExpiry = TimeSpan.FromSeconds(60);

    public async Task SweepAsync(CancellationToken ct = default)
    {
        var agencyIds = await dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.Level == OrganizationLevel.Agency)
            .Select(o => o.Id)
            .ToListAsync(ct);

        foreach (var agencyId in agencyIds)
        {
            AgencyContext.Current = agencyId;

            await presenceService.SweepExpiredAsync(PresenceExpiry, ct);

            await dbContext.RecordLocks
                .Where(l => l.ExpiresAt < DateTimeOffset.UtcNow && l.ForceReleasedAt == null)
                .ExecuteDeleteAsync(ct);
        }
    }
}
