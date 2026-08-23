using Aqsat.Application.Sms;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Jobs;

/// <summary>
/// docs/TASKS.md Task 16 (niaz #4), docs/PHASE-1-SPEC.md §2.9. Two responsibilities, run daily:
/// (1) auto-create a RenewalWatch as one of the agency's own policies approaches EndDate, and
/// (2) fire the reminder — to the customer AND their marketer — exactly on the day
/// daysUntilExpiry == NotifyDaysBefore, the same conditional-at-send-time idea SmsReminderJob uses
/// for installments (CLAUDE.md rule 25).
/// </summary>
public sealed class RenewalWatchJob(AppDbContext dbContext, ISmsSender smsSender, TimeProvider timeProvider)
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

            await CreateAutoWatchesAsync(agencyId, today, ct);
            await SendDueRemindersAsync(agencyId, today, ct);

            dbContext.ChangeTracker.Clear();
        }
    }

    private async Task CreateAutoWatchesAsync(Guid agencyId, DateOnly today, CancellationToken ct)
    {
        var org = await dbContext.Organizations.AsNoTracking()
            .Where(o => o.Id == agencyId).Select(o => o.InsurerName).FirstOrDefaultAsync(ct);
        var orgSettings = await dbContext.OrgSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == agencyId, ct);
        var leadDays = orgSettings?.RenewalAutoWatchLeadDays ?? 60;
        var horizon = today.AddDays(leadDays);

        var expiringPolicies = await dbContext.Policies
            .Where(p => p.Status == PolicyStatus.Active && p.EndDate >= today && p.EndDate <= horizon)
            .ToListAsync(ct);

        foreach (var policy in expiringPolicies)
        {
            // Dedup key: the expiring policy's own (customer, line, expiry) triple — PolicyId on
            // RenewalWatch is reserved for the NEW policy a conversion links to, so it can't double
            // as "which policy is this watch tracking".
            var alreadyWatched = await dbContext.RenewalWatches.AsNoTracking().AnyAsync(
                w => w.CustomerId == policy.CustomerId && w.InsuranceLineId == policy.InsuranceLineId
                    && w.CurrentExpiryDate == policy.EndDate, ct);
            if (alreadyWatched)
            {
                continue;
            }

            dbContext.RenewalWatches.Add(new RenewalWatch
            {
                AgencyId = agencyId,
                CustomerId = policy.CustomerId,
                InsuranceLineId = policy.InsuranceLineId,
                CurrentInsurer = org,
                CurrentExpiryDate = policy.EndDate,
                NotifyDaysBefore = 2,
                MarketerId = policy.MarketerId,
                Status = RenewalWatchStatus.Watching,
            });
        }

        if (expiringPolicies.Count > 0)
        {
            await SaveIgnoringDuplicatesAsync(ct);
        }
    }

    private async Task SendDueRemindersAsync(Guid agencyId, DateOnly today, CancellationToken ct)
    {
        var customBodies = await dbContext.SmsTemplates.AsNoTracking()
            .Where(t => t.Key == RenewalReminderTemplate.CustomerKey || t.Key == RenewalReminderTemplate.MarketerKey)
            .ToDictionaryAsync(t => t.Key, t => t.Body, ct);

        var candidates = await dbContext.RenewalWatches
            .Where(w => w.Status == RenewalWatchStatus.Watching || w.Status == RenewalWatchStatus.Notified)
            .Include(w => w.Customer)
            .Include(w => w.Marketer)
            .Include(w => w.InsuranceLine)
            .ToListAsync(ct);

        foreach (var watch in candidates)
        {
            var daysUntilExpiry = watch.CurrentExpiryDate.DayNumber - today.DayNumber;
            if (daysUntilExpiry != watch.NotifyDaysBefore)
            {
                continue;
            }

            var customerMobile = watch.Customer?.Mobile ?? watch.ProspectMobile;
            if (!string.IsNullOrWhiteSpace(customerMobile))
            {
                await SendIfNotAlreadySentAsync(
                    agencyId, watch, daysUntilExpiry, ReminderRecipientType.Customer, customerMobile,
                    customBodies.GetValueOrDefault(RenewalReminderTemplate.CustomerKey), ct);
            }

            if (watch.Marketer is { Mobile: var marketerMobile } && !string.IsNullOrWhiteSpace(marketerMobile))
            {
                await SendIfNotAlreadySentAsync(
                    agencyId, watch, daysUntilExpiry, ReminderRecipientType.Marketer, marketerMobile,
                    customBodies.GetValueOrDefault(RenewalReminderTemplate.MarketerKey), ct);
            }

            if (watch.Status == RenewalWatchStatus.Watching)
            {
                watch.Status = RenewalWatchStatus.Notified;
            }
        }

        if (candidates.Count > 0)
        {
            await SaveIgnoringDuplicatesAsync(ct);
        }
    }

    private async Task SendIfNotAlreadySentAsync(
        Guid agencyId, RenewalWatch watch, int offsetDays, ReminderRecipientType recipientType, string mobile,
        string? customBody, CancellationToken ct)
    {
        var alreadySent = await dbContext.ReminderLogs.AsNoTracking().AnyAsync(
            r => r.RenewalWatchId == watch.Id && r.OffsetDays == offsetDays && r.RecipientType == recipientType, ct);
        if (alreadySent)
        {
            return;
        }

        var text = RenewalReminderTemplate.Render(recipientType, watch.InsuranceLine?.NameFa, watch.CurrentExpiryDate, customBody);
        var sent = await smsSender.SendAsync(mobile, text, agencyId, ct);

        dbContext.ReminderLogs.Add(new ReminderLog
        {
            AgencyId = agencyId,
            RenewalWatchId = watch.Id,
            RecipientType = recipientType,
            Mobile = mobile,
            OffsetDays = offsetDays,
            TemplateKey = RenewalReminderTemplate.Key,
            Channel = ReminderChannel.Sms,
            Status = sent ? ReminderSendStatus.Sent : ReminderSendStatus.Failed,
            SentAt = DateTimeOffset.UtcNow,
        });
    }

    private async Task SaveIgnoringDuplicatesAsync(CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Same "duplicate = success" idempotency as SmsReminderJob — a concurrent run already
            // won the unique-index race.
            foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}

/// <summary>Two separately editable SmsTemplate keys (customer/marketer get different wording,
/// docs/PHASE-1-SPEC.md niaz #6/#13 — "to customers and to marketers"), both {LineName}/{ExpiryDate}
/// placeholders. ReminderLog.TemplateKey stays the single Key constant below regardless of which
/// SmsTemplate row rendered it — that field identifies the render function for dedup, not the
/// customized text.</summary>
public static class RenewalReminderTemplate
{
    public const string Key = "renewal-reminder-v1";

    public const string CustomerKey = "renewal-reminder-customer-v1";
    public const string MarketerKey = "renewal-reminder-marketer-v1";

    public const string CustomerDefaultBody = "بیمه‌گذار گرامی، بیمهٔ {LineName} شما تا تاریخ {ExpiryDate} سررسید تمدید دارد.";
    public const string MarketerDefaultBody = "بازاریاب گرامی، بیمهٔ {LineName} یکی از مشتریان معرفی‌شدهٔ شما تا تاریخ {ExpiryDate} سررسید تمدید دارد.";

    public static string Render(ReminderRecipientType recipientType, string? lineNameFa, DateOnly expiryDate, string? customBody = null)
    {
        var body = customBody ?? (recipientType == ReminderRecipientType.Marketer ? MarketerDefaultBody : CustomerDefaultBody);
        return body
            .Replace("{LineName}", lineNameFa)
            .Replace("{ExpiryDate}", expiryDate.ToString("yyyy-MM-dd"));
    }
}
