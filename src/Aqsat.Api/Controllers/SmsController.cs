using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Sms;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Jobs;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 14 / docs/PHASE-1-SPEC.md §3.7 (niaz #13) — the manual filtered send: filter
/// → preview count and cost → confirm → send. A manual send reuses the exact same
/// (InstallmentId, OffsetDays, RecipientType) idempotency key the daily job uses (OffsetDays =
/// days-until-due *at send time*) — a double-click today is caught by the same unique index; a
/// manual send tomorrow is a different key and goes through, no special-casing needed.
/// </summary>
[ApiController]
[Route("api/sms")]
[Authorize(Policy = Permissions.PolicyWrite)]
public sealed class SmsController(AppDbContext dbContext, ISmsSender smsSender, ICurrentUserContext currentUser) : ControllerBase
{
    private const decimal SendSmsCost = 115m;

    [HttpPost("preview")]
    public async Task<ActionResult<SmsPreviewResultDto>> Preview(SmsFilterRequest filter, CancellationToken ct)
    {
        var count = await BuildQuery(filter).CountAsync(ct);
        return Ok(new SmsPreviewResultDto(count, count * SendSmsCost));
    }

    [HttpPost("send")]
    public async Task<ActionResult<SmsSendResultDto>> Send(SmsFilterRequest filter, CancellationToken ct)
    {
        var installments = await BuildQuery(filter)
            .Include(i => i.Policy).ThenInclude(p => p.Customer)
            .ToListAsync(ct);

        var customBody = await dbContext.SmsTemplates.AsNoTracking()
            .Where(t => t.Key == InstallmentReminderTemplate.Key)
            .Select(t => t.Body)
            .FirstOrDefaultAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sentCount = 0;
        var skippedCount = 0;
        var alreadySentTodayCount = 0;

        foreach (var installment in installments)
        {
            var mobile = installment.Policy.Customer.Mobile;
            if (string.IsNullOrWhiteSpace(mobile))
            {
                skippedCount++;
                continue;
            }

            var daysUntilDue = installment.DueDate.DayNumber - today.DayNumber;
            var alreadySent = await dbContext.ReminderLogs.AsNoTracking().AnyAsync(
                r => r.InstallmentId == installment.Id && r.OffsetDays == daysUntilDue
                    && r.RecipientType == ReminderRecipientType.Customer,
                ct);
            if (alreadySent)
            {
                alreadySentTodayCount++;
                continue;
            }

            var text = InstallmentReminderTemplate.Render(
                installment.Policy.PolicyNumber, installment.SeqNo, installment.Balance, installment.DueDate, customBody);
            var sent = await smsSender.SendAsync(mobile, text, currentUser.ActiveOrganizationId, ct);

            dbContext.ReminderLogs.Add(new ReminderLog
            {
                AgencyId = currentUser.ActiveOrganizationId,
                InstallmentId = installment.Id,
                RecipientType = ReminderRecipientType.Customer,
                Mobile = mobile,
                OffsetDays = daysUntilDue,
                TemplateKey = InstallmentReminderTemplate.Key,
                Channel = ReminderChannel.Sms,
                Status = sent ? ReminderSendStatus.Sent : ReminderSendStatus.Failed,
                SentAt = DateTimeOffset.UtcNow,
            });

            if (sent)
            {
                sentCount++;
            }
            else
            {
                skippedCount++;
            }
        }

        await dbContext.SaveChangesAsync(ct);
        return Ok(new SmsSendResultDto(sentCount, skippedCount, alreadySentTodayCount));
    }

    /// <summary>The delivery report — docs/TASKS.md Task 14.</summary>
    [HttpGet("log")]
    public async Task<ActionResult<IReadOnlyList<ReminderLogDto>>> Log([FromQuery] int take, CancellationToken ct)
    {
        var effectiveTake = take is > 0 and <= 200 ? take : 50;

        var log = await dbContext.ReminderLogs
            .AsNoTracking()
            .Include(r => r.Installment).ThenInclude(i => i!.Policy)
            .OrderByDescending(r => r.SentAt)
            .Take(effectiveTake)
            .Select(r => new ReminderLogDto(
                r.Id, r.InstallmentId, r.Installment != null ? r.Installment.Policy.PolicyNumber : null,
                r.Installment != null ? r.Installment.SeqNo : (int?)null,
                r.RecipientType.ToString(), r.Mobile, r.OffsetDays, r.Status.ToString(), r.SentAt))
            .ToListAsync(ct);

        return Ok(log);
    }

    /// <summary>گزارش تحویل پیامک — a summary over the whole log, distinct from /log's paginated
    /// list which صندوق ارسال reads directly.</summary>
    [HttpGet("delivery-report")]
    public async Task<ActionResult<SmsDeliveryReportDto>> DeliveryReport(CancellationToken ct)
    {
        var logs = await dbContext.ReminderLogs.AsNoTracking().ToListAsync(ct);

        return Ok(new SmsDeliveryReportDto(
            logs.Count(l => l.Status == ReminderSendStatus.Sent),
            logs.Count(l => l.Status == ReminderSendStatus.Failed),
            logs.Count(l => l.RecipientType == ReminderRecipientType.Customer),
            logs.Count(l => l.RecipientType == ReminderRecipientType.Marketer),
            logs.Count(l => l.InstallmentId != null),
            logs.Count(l => l.RenewalWatchId != null)));
    }

    private IQueryable<Installment> BuildQuery(SmsFilterRequest filter)
    {
        var query = dbContext.Installments.AsNoTracking().Where(i => i.Status != InstallmentStatus.Settled);

        if (filter.DueFrom is { } dueFrom)
        {
            query = query.Where(i => i.DueDate >= dueFrom);
        }

        if (filter.DueTo is { } dueTo)
        {
            query = query.Where(i => i.DueDate <= dueTo);
        }

        if (filter.Status is not null && Enum.TryParse<InstallmentStatus>(filter.Status, out var status))
        {
            query = query.Where(i => i.Status == status);
        }

        if (filter.InsuranceLineId is { } lineId)
        {
            query = query.Where(i => i.Policy.InsuranceLineId == lineId);
        }

        if (filter.MarketerId is { } marketerId)
        {
            query = query.Where(i => i.Policy.MarketerId == marketerId);
        }

        if (filter.MinAmount is { } minAmount)
        {
            query = query.Where(i => i.Amount >= minAmount);
        }

        if (filter.MaxAmount is { } maxAmount)
        {
            query = query.Where(i => i.Amount <= maxAmount);
        }

        return query;
    }
}
