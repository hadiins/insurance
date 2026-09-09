using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Risk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aqsat.Infrastructure.Jobs;

/// <summary>
/// docs Phase 2A — the nightly assessment (owner decision 2026-09-03: manual + nightly). Every
/// agency's customers with at least one policy are re-assessed with Source = Scheduled, so risk
/// warnings, the dashboard trends, and the high-risk view stay current without anyone pressing
/// the button. Customers without evidence are skipped by AssessAsync's InsufficientData path —
/// it writes nothing, so filtering to policy-holders keeps the snapshot table clean.
/// </summary>
public sealed class RiskAssessmentJob(
    AppDbContext dbContext,
    RiskAssessmentService assessmentService,
    ILogger<RiskAssessmentJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var agencyIds = await dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.Level == OrganizationLevel.Agency)
            .Select(o => o.Id)
            .ToListAsync(ct);

        foreach (var agencyId in agencyIds)
        {
            // RLS: each agency's customers are only visible once the scope matches.
            AgencyContext.Current = agencyId;

            var customerIds = await dbContext.Policies
                .AsNoTracking()
                .Select(p => p.CustomerId)
                .Distinct()
                .ToListAsync(ct);

            foreach (var customerId in customerIds)
            {
                // One bad customer must not kill the run for everyone after it — the catch is
                // deliberately broad: a transient DB error on customer N would otherwise abort the
                // rest of this agency AND every agency after it, and the failure is logged, never
                // swallowed (rule 15).
                try
                {
                    await assessmentService.AssessAsync(
                        customerId, RiskAssessmentSource.Scheduled, Guid.Empty, "زمان‌بندی شبانهٔ سیستم", ct);
                }
                catch (RiskAssessmentException ex)
                {
                    logger.LogWarning(ex, "Nightly risk assessment skipped customer {CustomerId} of agency {AgencyId}: {Reason}",
                        customerId, agencyId, ex.Message);
                }
                catch (Exception ex)
                {
                    // A failed SaveChanges can leave half-tracked entities behind; without this the
                    // same poisoned entries would fail every later customer's save too.
                    dbContext.ChangeTracker.Clear();
                    logger.LogError(ex, "Nightly risk assessment failed for customer {CustomerId} of agency {AgencyId}; continuing with the next customer",
                        customerId, agencyId);
                }
            }

            // A failed plate sync must not starve the agencies still queued behind this one.
            try
            {
                await SyncPlateIndexAsync(agencyId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Nightly plate-index sync failed for agency {AgencyId}; continuing with the next agency",
                    agencyId);
            }

            dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Phase 2B-1 — plates are the second lookup key into the network. Insert-only: a plate→person
    /// mapping is a historical fact, so the sync skips anything already indexed (a car's previous
    /// owner stays searchable) and never rewrites. Only customers with an assessed network profile
    /// and a national-ID hash are indexed — a hash with no profile row could never be returned by
    /// a lookup anyway.
    /// </summary>
    private async Task SyncPlateIndexAsync(Guid agencyId, CancellationToken ct)
    {
        var pairs = await (from p in dbContext.Policies.AsNoTracking()
                           join v in dbContext.Vehicles.AsNoTracking() on p.VehicleId equals v.Id
                           where v.PlateNormalized != null
                           select new { v.PlateNormalized, p.CustomerId })
            .Distinct()
            .ToListAsync(ct);
        if (pairs.Count == 0)
        {
            return;
        }

        var hashByCustomer = await dbContext.NetworkRiskProfiles.AsNoTracking()
            .Where(n => n.AgencyId == agencyId)
            .ToDictionaryAsync(n => n.CustomerId, n => n.NationalIdHash, ct);

        var existingPlates = (await dbContext.NetworkRiskPlateIndex.AsNoTracking()
                .Where(i => i.AgencyId == agencyId)
                .Select(i => i.PlateNormalized)
                .ToListAsync(ct))
            .ToHashSet();

        var now = DateTimeOffset.UtcNow;
        foreach (var group in pairs.Where(x => hashByCustomer.ContainsKey(x.CustomerId))
                     .GroupBy(x => x.PlateNormalized))
        {
            if (existingPlates.Contains(group.Key))
            {
                continue;
            }

            dbContext.NetworkRiskPlateIndex.Add(new NetworkRiskPlateIndex
            {
                Id = SequentialGuidGenerator.Next(),
                AgencyId = agencyId,
                PlateNormalized = group.Key,
                NationalIdHash = hashByCustomer[group.First().CustomerId],
                SyncedAt = now,
            });
        }

        if (dbContext.ChangeTracker.Entries().Any(e => e.State == EntityState.Added))
        {
            await dbContext.SaveChangesAsync(ct);
        }
    }
}
