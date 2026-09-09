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

            var customBody = await dbContext.SmsTemplates.AsNoTracking()
                .Where(t => t.Key == InstallmentReminderTemplate.Key)
                .Select(t => t.Body)
                .FirstOrDefaultAsync(ct);

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

                // The online-payment link rides the reminder when the agency has opted into the
                // customer portal. Created-on-demand here (with its own immediate SaveChanges —
                // the catch below detaches this context's whole pending batch, so a half-created
                // link must never sit in it). A failure to mint the link must never cost the
                // customer their reminder — degrade to a plain-text reminder and log it (rule 15).
                string? paymentLink = null;
                if (orgSettings?.CustomerPortalEnabled == true)
                {
                    try
                    {
                        var link = await paymentLinkService.EnsureLinkAsync(
                            installment.Policy.CustomerId, Guid.Empty, ct);
                        paymentLink = $"{configuration["Portal:PublicBaseUrl"]?.TrimEnd('/')}/pay/{link.Token}";
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Failed to ensure payment link for customer {CustomerId}; reminder goes out without a link.",
                            installment.Policy.CustomerId);
                    }
                }

                var text = InstallmentReminderTemplate.Render(
                    installment.Policy.PolicyNumber, installment.SeqNo, installment.Balance,
                    installment.DueDate, customBody, paymentLink);
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
            .Replace("{PolicyNumber}", policyNumber)
            .Replace("{SeqNo}", seqNo.ToString())
            .Replace("{Balance}", balance.ToString("N0"))
            .Replace("{DueDate}", dueDate.ToString("yyyy-MM-dd"));

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
