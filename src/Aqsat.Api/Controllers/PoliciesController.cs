using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Schedule;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 8: schedule generation per §3.1/§3.2, with the "warning above
/// MaxInstallments" and per-item failure isolation for the "batch grid where the agent edits down
/// payment and count for many policies at once" — the batch endpoint never lets one bad row abort
/// the rest, matching the same principle Task 6/7's import pipeline already established.
/// </summary>
[ApiController]
[Route("api/policies")]
[Authorize(Policy = Permissions.PolicyWrite)]
public sealed class PoliciesController(AppDbContext dbContext, IHolidayChecker holidayChecker) : ControllerBase
{
    /// <summary>Imported policies flagged installment but not yet scheduled — the batch grid's
    /// worklist. InstallmentCount == 0 is the "not yet scheduled" marker Task 6/7's import commit
    /// leaves behind.</summary>
    [HttpGet("pending-schedule")]
    public async Task<ActionResult<IReadOnlyList<PendingSchedulePolicyDto>>> PendingSchedule(CancellationToken ct)
    {
        var policies = await dbContext.Policies
            .AsNoTracking()
            .Where(p => p.IsInstallment && p.InstallmentCount == 0)
            .Include(p => p.Customer)
            .OrderBy(p => p.PolicyNumber)
            .Select(p => new PendingSchedulePolicyDto(p.Id, p.PolicyNumber, p.Customer.FullName, p.ContractName, p.TotalPremium))
            .ToListAsync(ct);

        return Ok(policies);
    }

    [HttpGet("{id:guid}/suggest-down-payment")]
    public async Task<ActionResult<decimal>> SuggestDownPayment(Guid id, [FromQuery] int installmentCount, CancellationToken ct)
    {
        if (installmentCount <= 0)
        {
            return ValidationProblem("تعداد اقساط باید مثبت باشد.");
        }

        var policy = await dbContext.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null)
        {
            return NotFound();
        }

        return Ok(DownPaymentSuggester.Suggest(policy.TotalPremium, installmentCount));
    }

    [HttpPost("{id:guid}/schedule")]
    public async Task<ActionResult<ScheduleResultDto>> Schedule(Guid id, ScheduleRequest request, CancellationToken ct)
    {
        var (result, error) = await GenerateScheduleAsync(id, request.DownPayment, request.InstallmentCount, ct);
        return error is not null ? ValidationProblem(error) : Ok(result);
    }

    [HttpPost("schedule-batch")]
    public async Task<ActionResult<IReadOnlyList<BatchScheduleResultDto>>> ScheduleBatch(
        IReadOnlyList<BatchScheduleItem> items, CancellationToken ct)
    {
        var results = new List<BatchScheduleResultDto>();
        foreach (var item in items)
        {
            var (result, error) = await GenerateScheduleAsync(item.PolicyId, item.DownPayment, item.InstallmentCount, ct);
            results.Add(new BatchScheduleResultDto(item.PolicyId, result, error));
        }

        return Ok(results);
    }

    private async Task<(ScheduleResultDto? Result, string? Error)> GenerateScheduleAsync(
        Guid policyId, decimal downPayment, int installmentCount, CancellationToken ct)
    {
        var policy = await dbContext.Policies.FirstOrDefaultAsync(p => p.Id == policyId, ct);
        if (policy is null)
        {
            return (null, "بیمه‌نامه یافت نشد.");
        }

        if (policy.InstallmentCount > 0)
        {
            return (null, "این بیمه‌نامه قبلاً زمان‌بندی شده است.");
        }

        if (installmentCount <= 0)
        {
            return (null, "تعداد اقساط باید مثبت باشد.");
        }

        if (downPayment < 0 || downPayment >= policy.TotalPremium)
        {
            return (null, "پیش‌پرداخت نامعتبر است.");
        }

        var orgSettings = await dbContext.OrgSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == policy.AgencyId, ct);
        // OrgSettings may not exist yet for every agency — the entity's own defaults (rule: never
        // hard-coded constants in business logic) apply when it doesn't.
        var maxInstallments = orgSettings?.MaxInstallments ?? 9;
        var shiftOnHoliday = orgSettings?.ShiftOnHoliday ?? true;
        var deadlineDays = orgSettings?.SettlementDeadlineDays ?? 3;

        var amounts = InstallmentAmountCalculator.CalculateAmounts(policy.TotalPremium, downPayment, installmentCount);

        var installments = new List<Installment>(installmentCount);
        for (var seq = 1; seq <= installmentCount; seq++)
        {
            var dueDate = DueDateCalculator.CalculateDueDate(policy.StartDate, seq);
            var deadline = DueDateCalculator.CalculateSettlementDeadline(dueDate, deadlineDays, shiftOnHoliday, holidayChecker);

            installments.Add(new Installment
            {
                AgencyId = policy.AgencyId,
                PolicyId = policy.Id,
                SeqNo = seq,
                DueDate = dueDate,
                SettlementDeadline = deadline,
                Amount = amounts[seq - 1],
                Status = InstallmentStatus.Unpaid,
            });
        }

        dbContext.Installments.AddRange(installments);
        policy.DownPayment = downPayment;
        policy.InstallmentCount = installmentCount;
        await dbContext.SaveChangesAsync(ct);

        var dto = new ScheduleResultDto(
            policy.Id,
            installmentCount > maxInstallments,
            installments.Select(i => new InstallmentDto(i.SeqNo, i.DueDate, i.Amount)).ToList());

        return (dto, null);
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
