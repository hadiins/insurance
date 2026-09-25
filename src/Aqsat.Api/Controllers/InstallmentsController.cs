using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Countdown;
using Aqsat.Application.Schedule;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 9 (niaz #3) — an agent can override a generated installment's amount and/or
/// due date. The deadline is always recomputed from whatever DueDate ends up in effect (holiday
/// rule reapplied); the due date itself never shifts on its own (CLAUDE.md). Audit is automatic —
/// Installment already implements IAuditableEntity, so AppDbContext's SaveChangesAsync override
/// writes the row in the same transaction without any extra code here.
///
/// This list is also the Today dashboard's worklist, so it carries the collection chips
/// (<c>filter</c>) as well as the urgency buckets. The two are different questions: urgency is
/// derived from dates alone, a chip asks about the collection *action* taken (cheque held, promise
/// outstanding, nobody has called).
/// </summary>
[ApiController]
[Route("api/installments")]
[Authorize(Policy = Permissions.PolicyWrite)]
public sealed class InstallmentsController(AppDbContext dbContext, IHolidayChecker holidayChecker, TimeProvider timeProvider) : ControllerBase
{
    private const int WorklistPageSize = 300;

    /// <summary>An xlsx has no interactive "next page", so the export takes far more than a screen
    /// — but still a bound, and one it states out loud in the file rather than truncating in
    /// silence (CLAUDE.md: a silent cap reads as "this is everything").</summary>
    private const int ExportRowLimit = 5000;

    /// <summary>How far back «بدون تماس» looks for a logged call.</summary>
    private const int NoContactDays = 7;

    /// <summary>The worklist's collection chips. «همه» is the unfiltered list; the rest each add one
    /// predicate. The names are the wire contract — the client sends them back verbatim.</summary>
    private static readonly string[] WorklistFilters = ["all", "overdue", "cheque", "promise", "noContact"];

    /// <summary>Backs اقساط معوق / تسویه‌های جزئی. Unlike /api/countdown, this has no date window —
    /// every unsettled installment past its deadline, or every partially-paid one, regardless of
    /// how far in the past or future its due date sits — unless the caller narrows it with
    /// from/to. Rows come back ordered by due date (the date the customer must pay), not by the
    /// settlement deadline (the date the agency must remit).</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.PolicyRead)]
    public async Task<ActionResult<IReadOnlyList<InstallmentWorklistRowDto>>> List(
        [FromQuery] bool? overdueOnly, [FromQuery] string? status, [FromQuery] string? urgency,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? search,
        [FromQuery] string? filter, CancellationToken ct)
    {
        var today = IranClock.Today(timeProvider);

        if (!TryNormalizeFilter(filter, out var normalizedFilter))
        {
            return ValidationProblem($"فیلتر «{filter}» شناخته‌شده نیست.");
        }

        var classified = await ClassifyAsync(overdueOnly, status, urgency, from, to, search, normalizedFilter, today, ct);

        var page = classified.Take(WorklistPageSize).ToList();
        var seqTotals = await LoadSeqTotalsAsync(page.Select(c => c.Row.PolicyId), ct);

        return Ok(page.Select(c => ToRow(c, seqTotals)).ToList());
    }

    /// <summary>Counts and open balance per urgency bucket over every unsettled installment —
    /// backs the worklist's filter chips (with live counters) and its overdue banner. The chip
    /// counts come from the same predicates the list applies, so a chip can never promise a row
    /// the list will not return.</summary>
    [HttpGet("counts")]
    [Authorize(Policy = Permissions.PolicyRead)]
    public async Task<ActionResult<InstallmentCountsDto>> Counts(CancellationToken ct)
    {
        var today = IranClock.Today(timeProvider);

        var rows = await dbContext.Installments.AsNoTracking()
            .Where(i => i.Status != InstallmentStatus.Settled)
            .Select(i => new { i.DueDate, i.SettlementDeadline, i.Amount, i.PaidAmount })
            .ToListAsync(ct);

        var buckets = rows
            .GroupBy(i => CountdownUrgencyClassifier.Classify(today, i.DueDate, i.SettlementDeadline))
            .Select(g => new InstallmentUrgencyCountDto(
                g.Key.ToString(), g.Count(), g.Sum(i => i.Amount - i.PaidAmount)))
            .OrderBy(b => Array.IndexOf(Enum.GetValues<CountdownUrgency>(), Enum.Parse<CountdownUrgency>(b.Urgency)))
            .ToList();

        var filters = new List<InstallmentFilterCountDto>(WorklistFilters.Length);
        foreach (var filter in WorklistFilters)
        {
            var query = await ApplyFilterAsync(UnsettledQuery(), filter, today, ct);
            filters.Add(new InstallmentFilterCountDto(filter, await query.CountAsync(ct)));
        }

        return Ok(new InstallmentCountsDto(
            rows.Count,
            rows.Sum(i => i.Amount - i.PaidAmount),
            buckets,
            filters));
    }

    /// <summary>The same rows the list shows, as xlsx. Deliberately identical predicates (both go
    /// through <see cref="ClassifyAsync"/>) — an export that disagreed with the screen would be
    /// worse than no export. Marketers have no export at all; this endpoint sits behind
    /// <see cref="Permissions.PolicyWrite"/>, which they do not hold.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] bool? overdueOnly, [FromQuery] string? status, [FromQuery] string? urgency,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? search,
        [FromQuery] string? filter, CancellationToken ct)
    {
        if (!TryNormalizeFilter(filter, out var normalizedFilter))
        {
            return ValidationProblem($"فیلتر «{filter}» شناخته‌شده نیست.");
        }

        var today = IranClock.Today(timeProvider);
        var classified = await ClassifyAsync(overdueOnly, status, urgency, from, to, search, normalizedFilter, today, ct);

        var matching = classified.Count;
        var page = classified.Take(ExportRowLimit).ToList();
        var seqTotals = await LoadSeqTotalsAsync(page.Select(c => c.Row.PolicyId), ct);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("اقساط");
        sheet.RightToLeft = true;
        string[] headers =
            ["بیمه‌نامه", "بیمه‌گذار", "موبایل", "قسط", "از", "سررسید", "مهلت تسویه", "مبلغ", "پرداخت‌شده", "مانده", "وضعیت", "فوریت"];
        for (var col = 0; col < headers.Length; col++)
        {
            sheet.Cell(1, col + 1).Value = headers[col];
        }

        for (var row = 0; row < page.Count; row++)
        {
            var r = ToRow(page[row], seqTotals);
            var excelRow = row + 2;
            sheet.Cell(excelRow, 1).Value = r.PolicyNumber;
            sheet.Cell(excelRow, 2).Value = r.CustomerFullName;
            sheet.Cell(excelRow, 3).Value = r.CustomerMobile ?? string.Empty;
            sheet.Cell(excelRow, 4).Value = r.SeqNo;
            sheet.Cell(excelRow, 5).Value = r.SeqTotal;
            sheet.Cell(excelRow, 6).Value = r.DueDate.ToString("yyyy-MM-dd");
            sheet.Cell(excelRow, 7).Value = r.SettlementDeadline.ToString("yyyy-MM-dd");
            sheet.Cell(excelRow, 8).Value = r.Amount;
            sheet.Cell(excelRow, 9).Value = r.PaidAmount;
            sheet.Cell(excelRow, 10).Value = r.Balance;
            sheet.Cell(excelRow, 11).Value = r.Status;
            sheet.Cell(excelRow, 12).Value = r.Urgency;
        }

        if (matching > page.Count)
        {
            sheet.Cell(page.Count + 3, 1).Value =
                $"این فایل {page.Count} ردیف از {matching} ردیف منطبق را دارد؛ برای دیدن بقیه فیلترها را محدودتر کنید.";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"installments-{today:yyyyMMdd}.xlsx");
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InstallmentDetailDto>> Update(Guid id, UpdateInstallmentRequest request, CancellationToken ct)
    {
        if (request.Amount is null && request.DueDate is null)
        {
            return ValidationProblem("حداقل یکی از مبلغ یا تاریخ باید ارسال شود.");
        }

        var installment = await dbContext.Installments.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (installment is null)
        {
            return NotFound();
        }

        if (installment.Status == InstallmentStatus.Settled)
        {
            return ValidationProblem("قسط تسویه‌شده قابل ویرایش نیست.");
        }

        if (request.Amount is { } newAmount)
        {
            if (newAmount <= 0)
            {
                return ValidationProblem("مبلغ قسط باید مثبت باشد.");
            }

            if (newAmount < installment.PaidAmount)
            {
                return ValidationProblem("مبلغ جدید نمی‌تواند کمتر از مبلغ پرداخت‌شدهٔ این قسط باشد.");
            }

            installment.Amount = newAmount;
            installment.Status = installment.PaidAmount <= 0
                ? InstallmentStatus.Unpaid
                : installment.PaidAmount >= newAmount ? InstallmentStatus.Settled : InstallmentStatus.Partial;
        }

        if (request.DueDate is { } newDueDate)
        {
            var orgSettings = await dbContext.OrgSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.OrganizationId == installment.AgencyId, ct);
            var shiftOnHoliday = orgSettings?.ShiftOnHoliday ?? true;
            var deadlineDays = orgSettings?.SettlementDeadlineDays ?? 3;

            installment.DueDate = newDueDate;
            installment.SettlementDeadline = await DueDateCalculator.CalculateSettlementDeadlineAsync(
                newDueDate, deadlineDays, shiftOnHoliday, holidayChecker, ct);

            if (request.ShiftFollowing)
            {
                // Re-lay the remaining unsettled installments on the same monthly cadence, anchored
                // to the edited date (AddPersianMonths = the issuance due-date rule). Settled ones
                // are history and never move.
                var following = await dbContext.Installments
                    .Where(i => i.PolicyId == installment.PolicyId && i.SeqNo > installment.SeqNo
                        && i.Status != InstallmentStatus.Settled)
                    .ToListAsync(ct);
                foreach (var next in following)
                {
                    next.DueDate = DueDateCalculator.AddPersianMonths(newDueDate, next.SeqNo - installment.SeqNo);
                    next.SettlementDeadline = await DueDateCalculator.CalculateSettlementDeadlineAsync(
                        next.DueDate, deadlineDays, shiftOnHoliday, holidayChecker, ct);
                    next.IsManuallyEdited = true;
                }
            }
        }

        installment.IsManuallyEdited = true;

        // Forward-compatible with Task 13: recalculates this installment's commission slice if one
        // already exists. Nothing generates CommissionEntry rows yet in this pass, so today this
        // simply finds none and is a no-op.
        if (request.Amount is not null)
        {
            var commissionEntry = await dbContext.CommissionEntries.FirstOrDefaultAsync(c => c.InstallmentId == id, ct);
            if (commissionEntry is not null)
            {
                var policy = await dbContext.Policies.AsNoTracking().FirstAsync(p => p.Id == installment.PolicyId, ct);
                commissionEntry.BasePortion = policy.NetPremium * (installment.Amount / policy.TotalReceivable);
                commissionEntry.Amount = commissionEntry.BasePortion * commissionEntry.RatePercent / 100m;
            }
        }

        await dbContext.SaveChangesAsync(ct);

        return Ok(new InstallmentDetailDto(
            installment.Id, installment.SeqNo, installment.DueDate, installment.SettlementDeadline,
            installment.Amount, installment.PaidAmount, installment.Status.ToString(), installment.IsManuallyEdited));
    }

    /// <summary>Every unsettled installment — the base set for both the list and the chip counts.
    /// Never the whole table: an installment is open until fully settled, so a settled one is
    /// history, not work.</summary>
    private IQueryable<Installment> UnsettledQuery() =>
        dbContext.Installments.AsNoTracking().Where(i => i.Status != InstallmentStatus.Settled);

    /// <summary>An unknown chip is an error, not a silently-unfiltered list — returning everything
    /// for a filter the server did not understand would look exactly like a working filter that
    /// happened to match a lot (CLAUDE.md rule 15).</summary>
    private static bool TryNormalizeFilter(string? filter, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            normalized = "all";
            return true;
        }

        var match = WorklistFilters.FirstOrDefault(f => string.Equals(f, filter.Trim(), StringComparison.OrdinalIgnoreCase));
        normalized = match ?? string.Empty;
        return match is not null;
    }

    /// <summary>The collection chip predicates. Each resolves its policy set with one query and
    /// then folds it into the installment query — RLS on those tables is what keeps the sets inside
    /// the caller's agency, so no agency id is passed by hand here (rule 10).</summary>
    private async Task<IQueryable<Installment>> ApplyFilterAsync(
        IQueryable<Installment> query, string filter, DateOnly today, CancellationToken ct)
    {
        switch (filter)
        {
            case "overdue":
                return query.Where(i => i.SettlementDeadline < today);

            case "cheque":
            {
                // A cheque the agency is still waiting on — held by the customer or deposited. One
                // query for the policy ids rather than a join per row, so a policy with several
                // cheques still contributes once.
                var policyIds = await dbContext.PaymentCheques.AsNoTracking()
                    .Where(c => c.Status == CollateralStatus.Held || c.Status == CollateralStatus.AtBank)
                    .Select(c => c.PolicyId)
                    .Distinct()
                    .ToListAsync(ct);
                return query.Where(i => policyIds.Contains(i.PolicyId));
            }

            case "promise":
            {
                // A live promise: the policy's latest contact is a promise whose date has not
                // passed yet. "Broken" is derived, never stored — a promise whose date went by
                // simply stops matching, exactly as the countdown derives its state from dates
                // rather than from a status column.
                var policyIds = await dbContext.CollectionContacts.AsNoTracking()
                    .Where(c => c.Outcome == ContactOutcome.Promised && c.PromisedOn != null && c.PromisedOn >= today)
                    .Where(c => !dbContext.CollectionContacts.Any(later =>
                        later.PolicyId == c.PolicyId && later.OccurredAt > c.OccurredAt))
                    .Select(c => c.PolicyId)
                    .Distinct()
                    .ToListAsync(ct);
                return query.Where(i => policyIds.Contains(i.PolicyId));
            }

            case "noContact":
            {
                // Nobody has logged a call about this policy in the last week. Only Call counts — an
                // SMS is something the agency sent, not a contact that was made.
                var cutoff = timeProvider.GetUtcNow().AddDays(-NoContactDays);
                var contacted = await dbContext.CollectionContacts.AsNoTracking()
                    .Where(c => c.Channel == ContactChannel.Call && c.OccurredAt >= cutoff)
                    .Select(c => c.PolicyId)
                    .Distinct()
                    .ToListAsync(ct);
                return query.Where(i => !contacted.Contains(i.PolicyId));
            }

            default:
                return query;
        }
    }

    /// <summary>Applies every filter, then classifies urgency in memory — urgency is date
    /// arithmetic, not a column, so it cannot be pushed into SQL and the paging has to happen
    /// after it.</summary>
    private async Task<List<ClassifiedRow>> ClassifyAsync(
        bool? overdueOnly, string? status, string? urgency, DateOnly? from, DateOnly? to,
        string? search, string filter, DateOnly today, CancellationToken ct)
    {
        var query = await ApplyFilterAsync(UnsettledQuery(), filter, today, ct);

        if (overdueOnly == true)
        {
            query = query.Where(i => i.SettlementDeadline < today);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<InstallmentStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(i => i.Status == parsedStatus);
        }

        if (from is { } fromDate)
        {
            query = query.Where(i => i.DueDate >= fromDate);
        }

        if (to is { } toDate)
        {
            query = query.Where(i => i.DueDate <= toDate);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var normalized = DigitNormalizer.ToLatin(term);
            query = query.Where(i =>
                i.Policy.PolicyNumber.Contains(term)
                || i.Policy.Customer.FullName.Contains(term)
                || (i.Policy.Customer.Mobile != null && i.Policy.Customer.Mobile.Contains(normalized))
                || (i.Policy.Customer.NationalId != null && i.Policy.Customer.NationalId.Contains(normalized))
                || (i.Policy.Vehicle != null && i.Policy.Vehicle.PlateNormalized != null
                    && i.Policy.Vehicle.PlateNormalized.Contains(normalized)));
        }

        var rows = await query
            .Include(i => i.Policy).ThenInclude(p => p.Customer)
            .Include(i => i.Policy).ThenInclude(p => p.Vehicle)
            .OrderBy(i => i.DueDate).ThenBy(i => i.SeqNo)
            .ToListAsync(ct);

        var classified = rows
            .Select(i => new ClassifiedRow(i, CountdownUrgencyClassifier.Classify(today, i.DueDate, i.SettlementDeadline)))
            .ToList();

        if (!string.IsNullOrWhiteSpace(urgency)
            && Enum.TryParse<CountdownUrgency>(urgency, ignoreCase: true, out var parsedUrgency))
        {
            classified = classified.Where(c => c.Urgency == parsedUrgency).ToList();
        }

        return classified;
    }

    /// <summary>How many installments each of the page's policies has, in one grouped query — the
    /// «قسط ۳ از ۶» denominator. One query for the page, never one per row.</summary>
    private async Task<IReadOnlyDictionary<Guid, int>> LoadSeqTotalsAsync(IEnumerable<Guid> policyIds, CancellationToken ct)
    {
        var ids = policyIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await dbContext.Installments.AsNoTracking()
            .Where(i => ids.Contains(i.PolicyId))
            .GroupBy(i => i.PolicyId)
            .Select(g => new { PolicyId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.PolicyId, x => x.Total, ct);
    }

    private static InstallmentWorklistRowDto ToRow(ClassifiedRow c, IReadOnlyDictionary<Guid, int> seqTotals) =>
        new(c.Row.Id, c.Row.PolicyId, c.Row.Policy.PolicyNumber, c.Row.Policy.Customer.FullName, c.Row.Policy.Customer.Mobile,
            c.Row.SeqNo, c.Row.DueDate, c.Row.SettlementDeadline,
            c.Row.Amount, c.Row.PaidAmount, c.Row.Balance, c.Row.Status.ToString(),
            c.Urgency.ToString(),
            // Falling back to SeqNo keeps the label readable («قسط ۳ از ۳») if a policy somehow has
            // no countable rows; a zero would read as a broken count.
            seqTotals.TryGetValue(c.Row.PolicyId, out var total) ? total : c.Row.SeqNo);

    private sealed record ClassifiedRow(Installment Row, CountdownUrgency Urgency);

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
