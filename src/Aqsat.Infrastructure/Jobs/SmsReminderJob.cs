using Aqsat.Application.Sms;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Jobs;

/// <summary>
/// docs/PHASE-1-SPEC.md §3.7, CLAUDE.md rule 25 verbatim: conditional at send time, never
/// pre-scheduled. Each daily run only fires reminders for installments whose days-until-due
/// exactly matches one of OrgSettings.ReminderDaysBefore today — an installment settled since the
/// last run simply never matches again, which is the entire cost-control mechanism (this is not an
/// optimisation, it's what keeps a ~960-installment agency from paying for ~2880 SMS a month
/// instead of ~1300).
/// </summary>
public sealed class SmsReminderJob(AppDbContext dbContext, ISmsSender smsSender, TimeProvider timeProvider)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var agencyIds = await dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.Level == OrganizationLevel.Agency)
            .Select(o => o.Id)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        foreach (var agencyId in agencyIds)
        {
            AgencyContext.Current = agencyId;

            var orgSettings = await dbContext.OrgSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.OrganizationId == agencyId, ct);
            var offsets = ParseOffsets(orgSettings?.ReminderDaysBefore ?? "7,3,0");
            if (offsets.Count == 0)
            {
                continue;
            }

            var candidates = await dbContext.Installments
                .Where(i => i.Status != InstallmentStatus.Settled)
                .Include(i => i.Policy).ThenInclude(p => p.Customer)
                .ToListAsync(ct);

            foreach (var installment in candidates)
            {
                var daysUntilDue = installment.DueDate.DayNumber - today.DayNumber;
                if (!offsets.Contains(daysUntilDue))
                {
                    continue;
                }

                var alreadySent = await dbContext.ReminderLogs.AsNoTracking().AnyAsync(
                    r => r.InstallmentId == installment.Id && r.OffsetDays == daysUntilDue
                        && r.RecipientType == ReminderRecipientType.Customer,
                    ct);
                if (alreadySent)
                {
                    continue;
                }

                var mobile = installment.Policy.Customer.Mobile;
                if (string.IsNullOrWhiteSpace(mobile))
                {
                    continue;
                }

                var text = InstallmentReminderTemplate.Render(installment.Policy.PolicyNumber, installment.SeqNo, installment.Balance, installment.DueDate);
                var sent = await smsSender.SendAsync(mobile, text, agencyId, ct);

                dbContext.ReminderLogs.Add(new ReminderLog
                {
                    AgencyId = agencyId,
                    InstallmentId = installment.Id,
                    RecipientType = ReminderRecipientType.Customer,
                    Mobile = mobile,
                    OffsetDays = daysUntilDue,
                    TemplateKey = InstallmentReminderTemplate.Key,
                    Channel = ReminderChannel.Sms,
                    Status = sent ? ReminderSendStatus.Sent : ReminderSendStatus.Failed,
                    SentAt = DateTimeOffset.UtcNow,
                });
            }

            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent run (or a race with the manual-send endpoint) already logged the same
                // (installment, offset) — the unique index caught it. Same "duplicate = success"
                // idempotency as everywhere else in this codebase, not a failure worth surfacing.
                foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }

            dbContext.ChangeTracker.Clear();
        }
    }

    private static HashSet<int> ParseOffsets(string reminderDaysBefore) =>
        reminderDaysBefore
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var value) ? (int?)value : null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToHashSet();
}

/// <summary>Placeholder-driven, not a DB-configurable template — a template management screen is
/// out of this pass's scope.</summary>
public static class InstallmentReminderTemplate
{
    public const string Key = "installment-reminder-v1";

    public static string Render(string policyNumber, int seqNo, decimal balance, DateOnly dueDate) =>
        $"بیمه‌گذار گرامی، قسط شمارهٔ {seqNo} بیمه‌نامهٔ {policyNumber} به مبلغ {balance:N0} تومان تا تاریخ {dueDate:yyyy-MM-dd} سررسید دارد.";
}
