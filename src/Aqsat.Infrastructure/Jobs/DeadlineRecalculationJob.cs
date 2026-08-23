using Aqsat.Application.Schedule;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Jobs;

/// <summary>
/// docs/TASKS.md Task 9: "Hangfire job recalculating deadlines daily." Re-derives every open
/// installment's SettlementDeadline from its (immutable) DueDate using each agency's current
/// OrgSettings and the holiday calendar — so a changed SettlementDeadlineDays/ShiftOnHoliday
/// setting, or an updated holiday calendar, is reflected without anyone re-running Task 8's
/// schedule generation. The due date itself never moves (CLAUDE.md rule: "Holidays shift the
/// deadline.").
/// </summary>
public sealed class DeadlineRecalculationJob(AppDbContext dbContext, IHolidayChecker holidayChecker)
{
    public async Task RecalculateAsync(CancellationToken ct = default)
    {
        // Only Agency-level orgs carry installments — Regional/Headquarters are aggregate-only.
        var agencyIds = await dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.Level == OrganizationLevel.Agency)
            .Select(o => o.Id)
            .ToListAsync(ct);

        foreach (var agencyId in agencyIds)
        {
            // Each agency's rows are only visible on this connection once AgencyContext.Current
            // matches — RLS is not a per-query filter you can pass an argument to.
            AgencyContext.Current = agencyId;

            var orgSettings = await dbContext.OrgSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.OrganizationId == agencyId, ct);
            var deadlineDays = orgSettings?.SettlementDeadlineDays ?? 3;
            var shiftOnHoliday = orgSettings?.ShiftOnHoliday ?? true;

            var openInstallments = await dbContext.Installments
                .Where(i => i.Status != InstallmentStatus.Settled)
                .ToListAsync(ct);

            foreach (var installment in openInstallments)
            {
                installment.SettlementDeadline = await DueDateCalculator.CalculateSettlementDeadlineAsync(
                    installment.DueDate, deadlineDays, shiftOnHoliday, holidayChecker, ct);
            }

            if (dbContext.ChangeTracker.HasChanges())
            {
                await dbContext.SaveChangesAsync(ct);
            }

            dbContext.ChangeTracker.Clear();
        }
    }
}
