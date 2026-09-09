using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Countdown;
using Aqsat.Application.Schedule;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
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
/// </summary>
[ApiController]
[Route("api/installments")]
[Authorize(Policy = Permissions.PolicyWrite)]
public sealed class InstallmentsController(AppDbContext dbContext, IHolidayChecker holidayChecker, TimeProvider timeProvider) : ControllerBase
{
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
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var query = dbContext.Installments.AsNoTracking().Where(i => i.Status != InstallmentStatus.Settled).AsQueryable();

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

        // Urgency is classified in memory (it is date arithmetic, not a column), so it filters
        // after the fetch — hence the unbounded query above and the Take moving down here.
        var classified = rows
            .Select(i => new
            {
                Row = i,
                Urgency = CountdownUrgencyClassifier.Classify(today, i.DueDate, i.SettlementDeadline),
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(urgency)
            && Enum.TryParse<CountdownUrgency>(urgency, ignoreCase: true, out var parsedUrgency))
        {
            classified = classified.Where(c => c.Urgency == parsedUrgency).ToList();
        }

        var dtos = classified
            .Take(300)
            .Select(c => new InstallmentWorklistRowDto(
                c.Row.Id, c.Row.PolicyId, c.Row.Policy.PolicyNumber, c.Row.Policy.Customer.FullName, c.Row.Policy.Customer.Mobile,
                c.Row.SeqNo, c.Row.DueDate, c.Row.SettlementDeadline,
                c.Row.Amount, c.Row.PaidAmount, c.Row.Balance, c.Row.Status.ToString(),
                c.Urgency.ToString()))
            .ToList();

        return Ok(dtos);
    }

    /// <summary>Counts and open balance per urgency bucket over every unsettled installment —
    /// backs the worklist's filter chips (with live counters) and its overdue banner.</summary>
    [HttpGet("counts")]
    [Authorize(Policy = Permissions.PolicyRead)]
    public async Task<ActionResult<InstallmentCountsDto>> Counts(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

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

        return Ok(new InstallmentCountsDto(
            rows.Count,
            rows.Sum(i => i.Amount - i.PaidAmount),
            buckets));
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

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
