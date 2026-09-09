using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Sms;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Jobs;
using Aqsat.Infrastructure.Persistence;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

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
    /// <summary>A loose filter (e.g. an empty date range) would otherwise enqueue a real paid SMS
    /// to EVERY open installment in the agency in one request — at ~115 toman a message that is a
    /// genuine financial incident, not just a slow query. Preview shows the cost; Send refuses to
    /// cross this line so the agent must narrow the filter deliberately.</summary>
    private const int MaxRecipientsPerSend = 200;

    /// <summary>The effectiveness report's attribution window bounds — a reminder can only be
    /// called effective within a sane horizon (rule of thumb: the send-to-deadline span plus a
    /// collection buffer), and an unbounded one would attribute every eventual payment to every
    /// reminder.</summary>
    private const int MinAttributionDays = 1;
    private const int MaxAttributionDays = 30;

    [HttpPost("preview")]
    public async Task<ActionResult<SmsPreviewResultDto>> Preview(SmsFilterRequest filter, CancellationToken ct)
    {
        var count = await BuildQuery(filter).CountAsync(ct);
        return Ok(new SmsPreviewResultDto(count, count * SmsPricing.PerMessageToman));
    }

    [HttpPost("send")]
    public async Task<ActionResult<SmsSendResultDto>> Send(SmsFilterRequest filter, CancellationToken ct)
    {
        var installments = await BuildQuery(filter)
            .Include(i => i.Policy).ThenInclude(p => p.Customer)
            .Take(MaxRecipientsPerSend + 1)
            .ToListAsync(ct);

        if (installments.Count > MaxRecipientsPerSend)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = $"تعداد گیرندگان بیش از حد مجاز ({MaxRecipientsPerSend}) است؛ فیلتر را محدودتر کنید.",
            });
        }

        var customBody = await dbContext.SmsTemplates.AsNoTracking()
            .Where(t => t.Key == InstallmentReminderTemplate.Key)
            .Select(t => t.Body)
            .FirstOrDefaultAsync(ct);

        // One query for every already-sent key in the batch instead of an AnyAsync per installment.
        var installmentIdSet = installments.Select(i => i.Id).ToHashSet();
        var sentKeys = (await dbContext.ReminderLogs.AsNoTracking()
                .Where(r => r.InstallmentId != null && r.RecipientType == ReminderRecipientType.Customer
                    && installmentIdSet.Contains(r.InstallmentId.Value))
                .Select(r => new { r.InstallmentId, r.OffsetDays })
                .ToListAsync(ct))
            .Select(k => (k.InstallmentId, k.OffsetDays))
            .ToHashSet();

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
            if (sentKeys.Contains((installment.Id, daysUntilDue)))
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

    /// <summary>گزارش اثربخشی پیامک — whether reminders actually led to payment, so the agency can
    /// see the SMS spend's value and tune ReminderDaysBefore. Scope: customer installment reminders
    /// only (renewal watches and marketer texts carry no InstallmentId and never enter the query);
    /// the rate's denominator is successful sends only. Attribution: an allocation whose PaidOn
    /// falls within attributionDays of the send, any channel — a cash receipt the agent records the
    /// day after the SMS still means the SMS worked. Not causal: a customer might have paid anyway;
    /// the UI labels it "attributed", never "caused".</summary>
    [HttpGet("effectiveness-report")]
    public async Task<ActionResult<SmsEffectivenessReportDto>> EffectivenessReport(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int attributionDays, CancellationToken ct)
    {
        if (TryValidateEffectivenessQuery(from, to, ref attributionDays) is { } validation)
        {
            return validation;
        }

        var data = await ComputeEffectivenessAsync(from, to, ct);

        var sent = data.SentLogs.Count;
        var paid = data.SentLogs.Count(l => IsEffective(l, data, attributionDays));
        var summary = new SmsEffectivenessSummaryDto(
            sent, data.FailedCount, data.Installments.Count,
            paid, Rate(paid, sent),
            data.Installments.Keys.Sum(installmentId => AttributedAmount(installmentId, data, attributionDays)),
            sent * SmsPricing.PerMessageToman);

        var offsetRows = data.SentLogs
            .GroupBy(l => l.OffsetDays)
            .Select(g => new SmsEffectivenessOffsetRow(
                g.Key, g.Count(), g.Count(l => IsEffective(l, data, attributionDays)),
                Rate(g.Count(l => IsEffective(l, data, attributionDays)), g.Count())))
            .OrderByDescending(r => r.OffsetDays)
            .ToList();

        return Ok(new SmsEffectivenessReportDto(summary, offsetRows, GroupByJalaliMonth(data, attributionDays)));
    }

    /// <summary>The per-installment detail behind the effectiveness numbers — every reminded
    /// installment, what was sent for it and what it collected.</summary>
    [HttpGet("effectiveness-report/details")]
    public async Task<ActionResult<SmsEffectivenessDetailPageDto>> EffectivenessDetails(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int attributionDays,
        [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
    {
        if (TryValidateEffectivenessQuery(from, to, ref attributionDays) is { } validation)
        {
            return validation;
        }

        page = page <= 0 ? 1 : page;
        pageSize = pageSize is <= 0 or > 200 ? 50 : pageSize;

        var rows = BuildDetailRows(await ComputeEffectivenessAsync(from, to, ct), attributionDays);

        return Ok(new SmsEffectivenessDetailPageDto(
            rows.Count, rows.Skip((page - 1) * pageSize).Take(pageSize).ToList()));
    }

    [HttpGet("effectiveness-report/export")]
    public async Task<IActionResult> EffectivenessExport(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int attributionDays, CancellationToken ct)
    {
        if (TryValidateEffectivenessQuery(from, to, ref attributionDays) is { } validation)
        {
            return validation;
        }

        var data = await ComputeEffectivenessAsync(from, to, ct);
        var sent = data.SentLogs.Count;
        var paid = data.SentLogs.Count(l => IsEffective(l, data, attributionDays));
        var summary = new SmsEffectivenessSummaryDto(
            sent, data.FailedCount, data.Installments.Count,
            paid, Rate(paid, sent),
            data.Installments.Keys.Sum(installmentId => AttributedAmount(installmentId, data, attributionDays)),
            sent * SmsPricing.PerMessageToman);
        var rows = BuildDetailRows(data, attributionDays);

        using var workbook = new XLWorkbook();
        var summarySheet = workbook.Worksheets.Add("خلاصه");
        summarySheet.RightToLeft = true;
        var row = 1;
        void WriteSummary(string label, string value)
        {
            summarySheet.Cell(row, 1).Value = label;
            summarySheet.Cell(row, 2).Value = value;
            row++;
        }
        WriteSummary("یادآوری ارسال‌شده", summary.RemindersSent.ToString());
        WriteSummary("ارسال ناموفق", summary.RemindersFailed.ToString());
        WriteSummary("اقساط یادآوری‌شده", summary.DistinctInstallments.ToString());
        WriteSummary("یادآوری مؤثر (پرداخت در پنجره)", summary.PaidWithinWindow.ToString());
        WriteSummary("نرخ اثربخشی (٪)", summary.EffectivenessRate.ToString(CultureInfo.InvariantCulture));
        WriteSummary("وصول منتسب (تومان)", summary.AttributedCollectedToman.ToString("0"));
        WriteSummary("هزینهٔ تخمینی پیامک (تومان)", summary.EstimatedCostToman.ToString("0"));

        var offsetSheet = workbook.Worksheets.Add("به‌تفکیک آفست");
        offsetSheet.RightToLeft = true;
        string[] offsetHeaders = ["آفست (روز تا سررسید)", "ارسال", "مؤثر", "نرخ (٪)"];
        for (var col = 0; col < offsetHeaders.Length; col++)
        {
            offsetSheet.Cell(1, col + 1).Value = offsetHeaders[col];
        }
        var offsetRows = data.SentLogs
            .GroupBy(l => l.OffsetDays)
            .Select(g => new { Offset = g.Key, Sent = g.Count(), Paid = g.Count(l => IsEffective(l, data, attributionDays)) })
            .OrderByDescending(r => r.Offset)
            .ToList();
        for (var i = 0; i < offsetRows.Count; i++)
        {
            var r = offsetRows[i];
            offsetSheet.Cell(i + 2, 1).Value = r.Offset;
            offsetSheet.Cell(i + 2, 2).Value = r.Sent;
            offsetSheet.Cell(i + 2, 3).Value = r.Paid;
            offsetSheet.Cell(i + 2, 4).Value = (double)Rate(r.Paid, r.Sent);
        }

        var detailSheet = workbook.Worksheets.Add("جزئیات اقساط");
        detailSheet.RightToLeft = true;
        string[] headers = ["بیمه‌نامه", "بیمه‌گذار", "قسط", "سررسید", "تعداد یادآوری", "آفست‌ها",
            "اولین یادآوری", "اولین پرداخت پس از یادآوری", "فاصله تا پرداخت (روز)", "وصول در پنجره (تومان)", "وضعیت"];
        for (var col = 0; col < headers.Length; col++)
        {
            detailSheet.Cell(1, col + 1).Value = headers[col];
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var excelRow = i + 2;
            detailSheet.Cell(excelRow, 1).Value = r.PolicyNumber;
            detailSheet.Cell(excelRow, 2).Value = r.CustomerFullName;
            detailSheet.Cell(excelRow, 3).Value = r.SeqNo;
            detailSheet.Cell(excelRow, 4).Value = r.DueDate.ToString("yyyy-MM-dd");
            detailSheet.Cell(excelRow, 5).Value = r.ReminderCount;
            detailSheet.Cell(excelRow, 6).Value = r.Offsets;
            detailSheet.Cell(excelRow, 7).Value = r.FirstReminderAt.ToString("yyyy-MM-dd HH:mm");
            detailSheet.Cell(excelRow, 8).Value = r.FirstPaidOnAfterReminder?.ToString("yyyy-MM-dd") ?? "";
            detailSheet.Cell(excelRow, 9).Value = r.DaysToPay;
            detailSheet.Cell(excelRow, 10).Value = r.CollectedInWindowToman;
            detailSheet.Cell(excelRow, 11).Value = r.InstallmentStatus;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"sms-effectiveness-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx");
    }

    private ActionResult? TryValidateEffectivenessQuery(DateOnly from, DateOnly to, ref int attributionDays)
    {
        if (to < from)
        {
            return Invalid("بازهٔ تاریخ نامعتبر است.");
        }

        if (attributionDays == 0)
        {
            attributionDays = DefaultAttributionDays;
        }
        if (attributionDays is < MinAttributionDays or > MaxAttributionDays)
        {
            return Invalid(
                $"روزهای انتساب باید بین {MinAttributionDays} و {MaxAttributionDays} باشد.");
        }

        return null;
    }

    private const int DefaultAttributionDays = 7;
    private sealed record SentLog(Guid InstallmentId, int OffsetDays, DateTimeOffset SentAt);
    private sealed record AllocationRow(DateOnly PaidOn, decimal Amount);
    private sealed record InstallmentRow(
        string PolicyNumber, string CustomerFullName, int SeqNo, DateOnly DueDate, string Status);

    private sealed record EffectivenessData(
        List<SentLog> SentLogs, int FailedCount,
        Dictionary<Guid, InstallmentRow> Installments,
        ILookup<Guid, AllocationRow> Allocations);

    /// <summary>One load of everything the three effectiveness endpoints share — sent logs in the
    /// campaign window, their installments, and every live allocation on those installments. The
    /// window matching runs in memory (CollectionsReportController.Summary's approach): one
    /// agency's reminder volume makes a per-row SQL correlated query the slower option, not the
    /// faster one.</summary>
    private async Task<EffectivenessData> ComputeEffectivenessAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var fromInstant = from.ToDateTime(TimeOnly.MinValue);
        var toExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var logs = await dbContext.ReminderLogs.AsNoTracking()
            .Where(r => r.InstallmentId != null && r.RecipientType == ReminderRecipientType.Customer
                && r.SentAt >= fromInstant && r.SentAt < toExclusive)
            .Select(r => new { r.InstallmentId, r.OffsetDays, r.SentAt, r.Status })
            .ToListAsync(ct);

        var sentLogs = logs
            .Where(l => l.Status == ReminderSendStatus.Sent)
            .Select(l => new SentLog(l.InstallmentId!.Value, l.OffsetDays, l.SentAt))
            .ToList();
        var failedCount = logs.Count(l => l.Status == ReminderSendStatus.Failed);

        var installmentIds = sentLogs.Select(l => l.InstallmentId).Distinct().ToList();
        var installments = await dbContext.Installments.AsNoTracking()
            .Where(i => installmentIds.Contains(i.Id))
            .Select(i => new { i.Id, Row = new InstallmentRow(
                i.Policy.PolicyNumber, i.Policy.Customer.FullName, i.SeqNo, i.DueDate, i.Status.ToString()) })
            .ToDictionaryAsync(i => i.Id, i => i.Row, ct);

        var allocations = await dbContext.PaymentAllocations.AsNoTracking()
            .Where(a => installmentIds.Contains(a.InstallmentId) && !a.Payment.IsDeleted)
            .Select(a => new { a.InstallmentId, Row = new AllocationRow(a.Payment.PaidOn, a.Amount) })
            .ToListAsync(ct);

        return new EffectivenessData(
            sentLogs, failedCount, installments,
            allocations.ToLookup(a => a.InstallmentId, a => a.Row));
    }

    /// <summary>The attribution rule itself: an allocation landed within attributionDays of this
    /// send (both bounds inclusive — same-day payment counts, and so does the last day of the
    /// window). PaidOn and SentAt are both taken in the server's local frame, the same convention
    /// payment recording already uses, so the comparison never straddles zones.</summary>
    private static bool IsEffective(SentLog log, EffectivenessData data, int attributionDays)
    {
        var sentDate = DateOnly.FromDateTime(log.SentAt.LocalDateTime);
        var windowEnd = sentDate.AddDays(attributionDays);
        return data.Allocations[log.InstallmentId].Any(a => a.PaidOn >= sentDate && a.PaidOn <= windowEnd);
    }

    /// <summary>The toman actually attributed to a reminder campaign for one installment: only the
    /// allocations that fall inside at least one of its reminder windows.</summary>
    private static decimal AttributedAmount(Guid installmentId, EffectivenessData data, int attributionDays)
    {
        var windows = data.SentLogs
            .Where(l => l.InstallmentId == installmentId)
            .Select(l => (Start: DateOnly.FromDateTime(l.SentAt.LocalDateTime),
                End: DateOnly.FromDateTime(l.SentAt.LocalDateTime).AddDays(attributionDays)))
            .ToList();
        return data.Allocations[installmentId]
            .Where(a => windows.Any(w => a.PaidOn >= w.Start && a.PaidOn <= w.End))
            .Sum(a => a.Amount);
    }

    private static decimal Rate(int paid, int sent) => sent == 0 ? 0 : Math.Round(100m * paid / sent, 1);

    private static List<SmsEffectivenessDetailRow> BuildDetailRows(EffectivenessData data, int attributionDays) =>
        data.Installments
            .Select(kvp =>
            {
                var (installmentId, installment) = (kvp.Key, kvp.Value);
                var logs = data.SentLogs.Where(l => l.InstallmentId == installmentId).OrderBy(l => l.SentAt).ToList();
                var firstReminder = logs[0];
                var firstReminderDate = DateOnly.FromDateTime(firstReminder.SentAt.LocalDateTime);
                var firstPaidOn = data.Allocations[installmentId]
                    .Where(a => a.PaidOn >= firstReminderDate)
                    .Select(a => (DateOnly?)a.PaidOn)
                    .DefaultIfEmpty()
                    .Min();
                return new SmsEffectivenessDetailRow(
                    installment.PolicyNumber, installment.CustomerFullName, installment.SeqNo, installment.DueDate,
                    logs.Count,
                    string.Join("، ", logs.Select(l => l.OffsetDays).Distinct().OrderByDescending(o => o)),
                    firstReminder.SentAt, firstPaidOn,
                    firstPaidOn is { } paidOn ? paidOn.DayNumber - firstReminderDate.DayNumber : null,
                    AttributedAmount(installmentId, data, attributionDays),
                    installment.Status);
            })
            .OrderByDescending(r => r.FirstReminderAt)
            .ToList();

    private static readonly string[] JalaliMonthNames =
        ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];
    private static readonly PersianCalendar Persian = new();

    /// <summary>Monthly trend bucketed by Jalali months, chronological, gap months zero-filled —
    /// the same contract as ReportsController.GroupByJalaliMonth, so a quiet month reads as zero,
    /// not as missing data.</summary>
    private static IReadOnlyList<SmsEffectivenessMonthRow> GroupByJalaliMonth(EffectivenessData data, int attributionDays)
    {
        var buckets = new Dictionary<int, (int Sent, int Paid)>();
        foreach (var log in data.SentLogs)
        {
            var date = log.SentAt.LocalDateTime;
            var key = Persian.GetYear(date) * 12 + Persian.GetMonth(date) - 1;
            var (sent, paid) = buckets.TryGetValue(key, out var existing) ? existing : (0, 0);
            buckets[key] = (sent + 1, paid + (IsEffective(log, data, attributionDays) ? 1 : 0));
        }
        if (buckets.Count == 0)
        {
            return [];
        }

        var rows = new List<SmsEffectivenessMonthRow>();
        for (var key = buckets.Keys.Min(); key <= buckets.Keys.Max(); key++)
        {
            var (sent, paid) = buckets.TryGetValue(key, out var bucket) ? bucket : (0, 0);
            rows.Add(new SmsEffectivenessMonthRow(
                key.ToString(), $"{JalaliMonthNames[key % 12]} {key / 12}", sent, paid, Rate(paid, sent)));
        }
        return rows;
    }

    private ActionResult Invalid(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });

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
