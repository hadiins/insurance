using Aqsat.Application.Common;
using Aqsat.Application.Sms;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Portal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aqsat.Infrastructure.Jobs;

/// <summary>
/// docs/PHASE-1-SPEC.md §3.7, CLAUDE.md rule 25 verbatim: conditional at send time, never
/// pre-scheduled. Each daily run only fires reminders for installments whose days-until-due
/// exactly matches one of OrgSettings.ReminderDaysBefore today — an installment settled since the
/// last run simply never matches again, which is the entire cost-control mechanism (this is not an
/// optimisation, it's what keeps a ~960-installment agency from paying for ~2880 SMS a month
/// instead of ~1300).
/// </summary>
public sealed class SmsReminderJob(
    AppDbContext dbContext,
    ISmsSender smsSender,
    TimeProvider timeProvider,
    InstallmentPaymentLinkService paymentLinkService,
    IConfiguration configuration,
    ILogger<SmsReminderJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var agencyIds = await dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.Level == OrganizationLevel.Agency)
            .Select(o => o.Id)
            .ToListAsync(ct);

        var today = IranClock.Today(timeProvider);

        foreach (var agencyId in agencyIds)
        {
            // Scoped, restored after each agency: this loop reuses ONE DbContext whose pooled
            // connection may be held open across iterations — the interceptor re-stamps the
            // session context when the ambient agency changes, and the scope guarantees a
            // later iteration (or whatever runs after RunAsync) never inherits a stale agency.
            using var agencyScope = AgencyContext.BeginScope(agencyId);

            var orgSettings = await dbContext.OrgSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.OrganizationId == agencyId, ct);
            var offsets = ParseOffsets(orgSettings?.ReminderDaysBefore ?? "7,3,0");
            if (offsets.Count == 0)
            {
                continue;
            }

            var customBody = await dbContext.SmsTemplates.AsNoTracking()
                .Where(t => t.Key == InstallmentReminderTemplate.Key)
                .Select(t => t.Body)
                .FirstOrDefaultAsync(ct);

            // ONE query for the whole day, not "load everything with Include, then a separate
            // query per installment" (the N+1 the old loop had via the per-installment
            // alreadySent check). Project straight into what the SMS needs, so a ~960-installment
            // agency stops loading full Policy+Customer graphs it never reads.
            var todayDayNumber = today.DayNumber;
            var maxOffset = offsets.Max();

            // DayNumber is not translatable for DateOnly on SQL Server, and the bound is more useful
            // as real dates anyway: a range on the column itself is sargable, so SQL Server can seek
            // an index on DueDate instead of scanning every unsettled installment in the agency.
            // The upper bound is "the last day an offset can still be ahead of us".
            var latestRelevantDue = today.AddDays(maxOffset);

            var candidates = await dbContext.Installments
                .AsNoTracking()
                .Where(i => i.Status != InstallmentStatus.Settled
                            && i.DueDate <= latestRelevantDue)
                .Select(i => new
                {
                    i.Id,
                    i.SeqNo,
                    i.Balance,
                    i.DueDate,
                    PolicyNumber = i.Policy.PolicyNumber,
                    CustomerId = i.Policy.CustomerId,
                    Mobile = i.Policy.Customer.Mobile,
                })
                .ToListAsync(ct);

            // The whole day's "already sent" set in ONE round trip, instead of one EXISTS query
            // per candidate installment. Bounded by OffsetDays + recency rather than an IN-list of
            // every candidate id: a given (installment, offset) pair can only ever match on ONE
            // calendar day (the offset is derived from the immutable DueDate minus today), so any
            // log for a current offset is at most a day or two old — an old row can never be the
            // one that suppresses today's send. This also keeps the statement well under SQL
            // Server's 2100-parameter limit for a large agency.
            // SentAt is a DateTimeOffset, so the recency floor is the START of that Iranian day in
            // UTC — inclusive, so a log written at 00:05 Tehran time is never excluded by rounding.
            var oldestRelevantUtc = new DateTimeOffset(
                today.AddDays(-14).ToDateTime(TimeOnly.MinValue), IranClock.Offset);
            var alreadySentOffsets = await dbContext.ReminderLogs.AsNoTracking()
                .Where(r => r.InstallmentId != null
                            && r.RecipientType == ReminderRecipientType.Customer
                            && offsets.Contains(r.OffsetDays)
                            && r.SentAt >= oldestRelevantUtc)
                .Select(r => new { r.InstallmentId, r.OffsetDays })
                .ToListAsync(ct);
            var alreadySent = alreadySentOffsets
                .Where(r => r.InstallmentId.HasValue)
                .GroupBy(r => r.InstallmentId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => (HashSet<int>)g.Select(x => x.OffsetDays).ToHashSet());

            foreach (var installment in candidates)
            {
                var daysUntilDue = installment.DueDate.DayNumber - todayDayNumber;
                var sentOffsets = alreadySent.TryGetValue(installment.Id, out var found)
                    ? found
                    : [];

                // Normally the exact configured offset, on exactly that day. But "exactly" made the
                // reminder fragile: if the host was down, a deploy skipped a run, or the send itself
                // failed, that day passed and the customer was never reminded about that offset
                // again — silently. So when today is PAST a configured offset, the smallest
                // already-passed offset that was never sent is sent now, as a single catch-up.
                // Cost stays bounded: idempotency is still keyed on (installment, offset), so each
                // missed reminder costs at most one catch-up SMS and can never repeat.
                var offset = daysUntilDue;
                if (!offsets.Contains(offset))
                {
                    var missed = offsets
                        .Where(o => o > daysUntilDue && !sentOffsets.Contains(o))
                        .DefaultIfEmpty()
                        .Min();
                    if (missed == 0)
                    {
                        continue;
                    }

                    offset = missed;
                }
                else if (sentOffsets.Contains(offset))
                {
                    continue;
                }

                var mobile = installment.Mobile;
                if (string.IsNullOrWhiteSpace(mobile))
                {
                    continue;
                }

                // The online-payment link rides the reminder when the agency has opted into the
                // customer portal. A failure to mint the link must never cost the customer their
                // reminder — degrade to a plain-text reminder and log it (rule 15).
                string? paymentLink = null;
                if (orgSettings?.CustomerPortalEnabled == true)
                {
                    try
                    {
                        var link = await paymentLinkService.EnsureLinkAsync(
                            installment.CustomerId, Guid.Empty, ct);
                        paymentLink = $"{configuration["Portal:PublicBaseUrl"]?.TrimEnd('/')}/pay/{link.Token}";
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Failed to ensure payment link for customer {CustomerId}; reminder goes out without a link.",
                            installment.CustomerId);
                    }
                }

                var text = InstallmentReminderTemplate.Render(
                    installment.PolicyNumber, installment.SeqNo, installment.Balance,
                    installment.DueDate, customBody, paymentLink);
                var sent = await smsSender.SendAsync(mobile, text, agencyId, ct);

                dbContext.ReminderLogs.Add(new ReminderLog
                {
                    AgencyId = agencyId,
                    InstallmentId = installment.Id,
                    RecipientType = ReminderRecipientType.Customer,
                    Mobile = mobile,
                    OffsetDays = offset,
                    TemplateKey = InstallmentReminderTemplate.Key,
                    Channel = ReminderChannel.Sms,
                    Status = sent ? ReminderSendStatus.Sent : ReminderSendStatus.Failed,
                    SentAt = DateTimeOffset.UtcNow,
                });

                // Persist THIS reminder before sending the next one. The old code accumulated every
                // agency's reminders in one pending batch and saved once at the end: a single duplicate
                // (installment, offset) then rolled the whole batch back, and the catch below detached
                // the entire batch — silently erasing the log of SMS messages that had already been
                // sent and paid for. Each log is its own unit of work now, so a duplicate costs only
                // its own row, exactly as a race elsewhere in this codebase is handled.
                try
                {
                    await dbContext.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // Duplicate = success (the unique index caught a concurrent run or the manual-send
                    // endpoint). Detach ONLY this one entry; nothing else is pending anymore.
                    dbContext.ChangeTracker.Clear();
                }
            }

            // No trailing batch save: every reminder above committed its own log inside the loop.
            // This final Clear just drops the read-only projections' tracker entries.
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

/// <summary>{PolicyNumber}/{SeqNo}/{Balance}/{DueDate}/{PaymentLink} placeholders — an agency's own
/// SmsTemplate row (Key = installment-reminder-v1) overrides DefaultBody; absence of a row falls
/// back to it, so customizing a template is optional, never required for sending to keep working.
/// {PaymentLink} is only replaced when the reminder carried a link (agency portal enabled); a
/// template without the placeholder simply never shows one.</summary>
public static class InstallmentReminderTemplate
{
    public const string Key = "installment-reminder-v1";

    public const string DefaultBody =
        "بیمه‌گذار گرامی، قسط شمارهٔ {SeqNo} بیمه‌نامهٔ {PolicyNumber} به مبلغ {Balance} تومان تا تاریخ {DueDate} سررسید دارد.";

    public static string Render(
        string policyNumber, int seqNo, decimal balance, DateOnly dueDate,
        string? customBody = null, string? paymentLink = null)
    {
        var body = (customBody ?? DefaultBody)
            .Replace("{PolicyNumber}", PersianText.ToPersianDigits(policyNumber))
            .Replace("{SeqNo}", PersianText.ToPersianDigits(seqNo.ToString()))
            .Replace("{Balance}", PersianText.ToPersianDigits(balance.ToString("N0")))
            // Jalali + Persian digits, NOT the old invariant "yyyy-MM-dd": the customer was being
            // texted a Gregorian date with Latin digits, which no other surface of this product shows.
            .Replace("{DueDate}", PersianText.JalaliDate(dueDate));

        if (paymentLink is null)
        {
            return body;
        }

        // The default body carries no {PaymentLink} slot (it must stay valid for agencies with the
        // portal off), so the link is appended as its own line; a custom template that already has
        // the placeholder gets it substituted in place instead.
        return body.Contains("{PaymentLink}")
            ? body.Replace("{PaymentLink}", paymentLink)
            : $"{body}\nپرداخت آنلاین: {paymentLink}";
    }
}
