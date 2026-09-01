using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Stats;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Jobs;

/// <summary>
/// Populates AgencyStatsDaily, the RLS-free rollup the owner's 60k-agency dashboard reads. The
/// nightly run recomputes a trailing window (recent activity edits — a payment recorded late, an
/// import corrected); the monthly full rebuild catches backdated data (Fanavaran imports carry
/// historical IssueDates, which a trailing window would never see). Both are idempotent upserts:
/// a day whose recomputed numbers drop to zero is updated to zero, never deleted (rule 7).
/// </summary>
public sealed class AgencyStatsRollupJob(
    AppDbContext dbContext, AgencyStatsService statsService, TimeProvider timeProvider)
{
    private const int TrailingDays = 90;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var today = Today();
        var hasAnyRows = await dbContext.AgencyStatsDaily.AsNoTracking().AnyAsync(ct);
        // First run after the migration: nothing exists yet, so a trailing window would leave the
        // whole history unrolled — build everything once, then nightly runs stay trailing.
        var from = hasAnyRows ? today.AddDays(-(TrailingDays - 1)) : DateOnly.MinValue;
        await RollupRangeAsync(from, today, ct);
    }

    public async Task RunFullRebuildAsync(CancellationToken ct = default)
    {
        await RollupRangeAsync(DateOnly.MinValue, Today(), ct);
    }

    private async Task RollupRangeAsync(DateOnly requestedFrom, DateOnly to, CancellationToken ct)
    {
        var agencyIds = await dbContext.Organizations.AsNoTracking()
            .Where(o => o.Level == OrganizationLevel.Agency)
            .Select(o => o.Id)
            .ToListAsync(ct);

        foreach (var agencyId in agencyIds)
        {
            ct.ThrowIfCancellationRequested();

            var from = requestedFrom;
            if (from == DateOnly.MinValue)
            {
                // Full rebuild starts at this agency's own earliest activity — most of the 60k
                // agencies will be brand new and skip straight past.
                var earliest = await statsService.GetEarliestActivityAsync(agencyId, ct);
                if (earliest is null)
                {
                    continue;
                }

                from = earliest.Value;
            }

            var daily = await statsService.ComputeDailyAsync(agencyId, from, to, ct);
            await UpsertAsync(agencyId, daily, ct);
        }
    }

    private async Task UpsertAsync(Guid agencyId, IReadOnlyList<AgencyDailyStats> daily, CancellationToken ct)
    {
        if (daily.Count == 0)
        {
            return;
        }

        var firstDay = daily[0].StatDate;
        var lastDay = daily[^1].StatDate;
        var existing = await dbContext.AgencyStatsDaily
            .Where(s => s.AgencyId == agencyId && s.StatDate >= firstDay && s.StatDate <= lastDay)
            .ToDictionaryAsync(s => s.StatDate, ct);

        foreach (var row in daily)
        {
            if (existing.TryGetValue(row.StatDate, out var stored))
            {
                stored.PoliciesIssued = row.PoliciesIssued;
                stored.SmsSentCount = row.SmsSentCount;
                stored.SmsCostToman = row.SmsCostToman;
                stored.InquiryPaymentsCount = row.InquiryPaymentsCount;
                stored.InquiryRevenueToman = row.InquiryRevenueToman;
                stored.InquiryCallsCount = row.InquiryCallsCount;
                stored.InquiryCallCostToman = row.InquiryCallCostToman;
            }
            else
            {
                dbContext.AgencyStatsDaily.Add(new AgencyStatsDaily
                {
                    AgencyId = agencyId,
                    StatDate = row.StatDate,
                    PoliciesIssued = row.PoliciesIssued,
                    SmsSentCount = row.SmsSentCount,
                    SmsCostToman = row.SmsCostToman,
                    InquiryPaymentsCount = row.InquiryPaymentsCount,
                    InquiryRevenueToman = row.InquiryRevenueToman,
                    InquiryCallsCount = row.InquiryCallsCount,
                    InquiryCallCostToman = row.InquiryCallCostToman,
                });
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two overlapping runs (nightly racing the manual full rebuild) hit the
            // (AgencyId, StatDate) unique index — the other run's numbers are just as correct.
            // Same "duplicate = success" idempotency as SmsReminderJob, logged not swallowed.
            foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        dbContext.ChangeTracker.Clear();
    }

    private DateOnly Today() =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(TimeSpan.FromHours(3.5)).DateTime);
}
