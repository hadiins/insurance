using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Schedule;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 6 (manual issuance) + Task 8 (schedule generation per §3.1/§3.2). The
/// "warning above MaxInstallments" and per-item failure isolation for the "batch grid where the
/// agent edits down payment and count for many policies at once" — the batch endpoint never lets
/// one bad row abort the rest, matching the same principle Task 6/7's import pipeline established.
/// </summary>
[ApiController]
[Route("api/policies")]
[Authorize(Policy = Permissions.PolicyWrite)]
public sealed class PoliciesController(
    AppDbContext dbContext, IHolidayChecker holidayChecker, ICurrentUserContext currentUser, IFieldEncryptor fieldEncryptor)
    : ControllerBase
{
    /// <summary>
    /// docs/TASKS.md Task 6 — manual issuance, fixed field order per docs/PHASE-1-SPEC.md §5. Only
    /// creates the Policy (+ Customer/Vehicle/PropertySubject as needed) — down payment and
    /// installment count are a separate step via the existing Schedule endpoint, not duplicated
    /// here. RequiresVehicle/RequiresProperty is enforced here, not just in the frontend
    /// (CLAUDE.md: "a service-layer rule keyed off InsuranceLine.RequiresVehicle").
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreatePolicyResultDto>> Create(CreatePolicyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PolicyNumber))
        {
            return ValidationProblem("شمارهٔ بیمه‌نامه الزامی است.");
        }

        var line = await dbContext.InsuranceLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == request.InsuranceLineId, ct);
        if (line is null)
        {
            return ValidationProblem("رشتهٔ بیمه یافت نشد.");
        }

        if (line.RequiresVehicle && IsEmptyVehicle(request.Vehicle))
        {
            return ValidationProblem("این رشتهٔ بیمه نیاز به مشخصات خودرو دارد.");
        }

        if (line.RequiresProperty && (request.Property is null || string.IsNullOrWhiteSpace(request.Property.Address)))
        {
            return ValidationProblem("این رشتهٔ بیمه نیاز به مشخصات ملک دارد.");
        }

        if (request.NetPremium <= 0)
        {
            return ValidationProblem("حق بیمه باید مثبت باشد.");
        }

        var agencyId = currentUser.ActiveOrganizationId;

        Guid customerId;
        if (request.CustomerId is { } existingCustomerId)
        {
            var exists = await dbContext.Customers.AsNoTracking().AnyAsync(c => c.Id == existingCustomerId, ct);
            if (!exists)
            {
                return ValidationProblem("بیمه‌گذار یافت نشد.");
            }

            customerId = existingCustomerId;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.CustomerFullName))
            {
                return ValidationProblem("نام بیمه‌گذار الزامی است.");
            }

            var customer = new Customer
            {
                AgencyId = agencyId,
                // No natural join key for a manually-entered customer (unlike an import row) — a
                // generated code just satisfies the NOT NULL + unique constraint.
                ExternalCode = $"MAN-{Guid.NewGuid():N}"[..12],
                FullName = request.CustomerFullName.Trim(),
                Mobile = request.CustomerMobile,
            };

            if (!string.IsNullOrWhiteSpace(request.CustomerNationalId))
            {
                customer.NationalId = request.CustomerNationalId;
                customer.NationalIdHash = fieldEncryptor.Hash(request.CustomerNationalId);
            }

            dbContext.Customers.Add(customer);
            await dbContext.SaveChangesAsync(ct);
            customerId = customer.Id;
        }

        Guid? vehicleId = null;
        if (!IsEmptyVehicle(request.Vehicle))
        {
            var vehicle = new Vehicle
            {
                AgencyId = agencyId,
                Plate = request.Vehicle!.Plate,
                Vin = request.Vehicle.Vin,
                Chassis = request.Vehicle.Chassis,
                Make = request.Vehicle.Make,
                Model = request.Vehicle.Model,
                Year = request.Vehicle.Year,
            };
            dbContext.Vehicles.Add(vehicle);
            await dbContext.SaveChangesAsync(ct);
            vehicleId = vehicle.Id;
        }

        Guid? propertySubjectId = null;
        if (request.Property is not null && !string.IsNullOrWhiteSpace(request.Property.Address))
        {
            var property = new PropertySubject
            {
                AgencyId = agencyId,
                Address = request.Property.Address.Trim(),
                PostalCode = request.Property.PostalCode,
                Type = request.Property.Type,
                Value = request.Property.Value,
            };
            dbContext.PropertySubjects.Add(property);
            await dbContext.SaveChangesAsync(ct);
            propertySubjectId = property.Id;
        }

        // The marketer's rate is locked at issuance (docs/PHASE-1-SPEC.md §2.2/§3.5) — a later rate
        // change must never alter this policy. If no rate is configured yet for this marketer/line
        // (Task 13 is not built in this pass), the policy is still created; commission generation is
        // simply deferred until a rate exists.
        decimal? marketerRatePercent = null;
        if (request.MarketerId is { } marketerId)
        {
            marketerRatePercent = await dbContext.MarketerRates
                .AsNoTracking()
                .Where(r => r.MarketerId == marketerId && r.InsuranceLineId == request.InsuranceLineId
                    && r.EffectiveFrom <= request.IssueDate && (r.EffectiveTo == null || r.EffectiveTo >= request.IssueDate))
                .OrderByDescending(r => r.EffectiveFrom)
                .Select(r => (decimal?)r.RatePercent)
                .FirstOrDefaultAsync(ct);
        }

        var policy = new Policy
        {
            AgencyId = agencyId,
            PolicyNumber = request.PolicyNumber.Trim(),
            InsuranceLineId = request.InsuranceLineId,
            CustomerId = customerId,
            VehicleId = vehicleId,
            PropertySubjectId = propertySubjectId,
            // Manually issued policies have no Fanavaran-style contract name to match against a
            // template — IsInstallment is simply true by construction (the agent chose this flow).
            ContractName = "صدور دستی",
            IsInstallment = true,
            IssueDate = request.IssueDate,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            NetPremium = request.NetPremium,
            ServiceFee = request.ServiceFee,
            DownPayment = 0,
            InstallmentCount = 0,
            MarketerId = request.MarketerId,
            MarketerRatePercent = marketerRatePercent,
            PreviousInsurer = request.PreviousInsurer,
            IsRenewal = request.IsRenewal,
        };
        dbContext.Policies.Add(policy);
        await dbContext.SaveChangesAsync(ct);

        return Ok(new CreatePolicyResultDto(policy.Id, policy.PolicyNumber, customerId));
    }

    private static bool IsEmptyVehicle(VehicleInput? vehicle) =>
        vehicle is null || (string.IsNullOrWhiteSpace(vehicle.Plate) && string.IsNullOrWhiteSpace(vehicle.Vin)
            && string.IsNullOrWhiteSpace(vehicle.Chassis));

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
            .Select(p => new PendingSchedulePolicyDto(p.Id, p.PolicyNumber, p.Customer.FullName, p.ContractName, p.NetPremium + p.ServiceFee))
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

        return Ok(DownPaymentSuggester.Suggest(policy.TotalReceivable, installmentCount));
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

        if (downPayment < 0 || downPayment >= policy.TotalReceivable)
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

        var amounts = InstallmentAmountCalculator.CalculateAmounts(policy.TotalReceivable, downPayment, installmentCount);

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
