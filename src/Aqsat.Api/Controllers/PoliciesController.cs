using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Commission;
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
                var normalizedNationalId = DigitNormalizer.ToLatin(request.CustomerNationalId).Trim();
                if (!NationalIdValidator.IsValid(normalizedNationalId))
                {
                    return ValidationProblem("کد ملی وارد شده نامعتبر است.");
                }

                customer.NationalId = normalizedNationalId;
                customer.NationalIdHash = fieldEncryptor.Hash(normalizedNationalId);
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
            AgencyCommissionPercent = request.AgencyCommissionPercent,
            AgencyCommissionAmount = request.AgencyCommissionPercent is { } pct ? request.NetPremium * pct / 100m : null,
        };
        dbContext.Policies.Add(policy);
        await dbContext.SaveChangesAsync(ct);

        return Ok(new CreatePolicyResultDto(policy.Id, policy.PolicyNumber, customerId));
    }

    /// <summary>در انتظار تأیید مشتری — the agent flags a freshly-issued policy as awaiting the
    /// customer's sign-off. Purely an internal worklist marker; nothing customer-facing changes.</summary>
    [HttpPut("{id:guid}/mark-pending-confirmation")]
    public async Task<ActionResult> MarkPendingConfirmation(Guid id, CancellationToken ct)
    {
        var policy = await dbContext.Policies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null)
        {
            return NotFound();
        }

        if (policy.Status != PolicyStatus.Active)
        {
            return ValidationProblem("فقط بیمه‌نامهٔ فعال قابل انتقال به «در انتظار تأیید» است.");
        }

        policy.Status = PolicyStatus.PendingConfirmation;
        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>The customer confirmed — back to Active.</summary>
    [HttpPut("{id:guid}/confirm")]
    public async Task<ActionResult> Confirm(Guid id, CancellationToken ct)
    {
        var policy = await dbContext.Policies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null)
        {
            return NotFound();
        }

        if (policy.Status != PolicyStatus.PendingConfirmation)
        {
            return ValidationProblem("این بیمه‌نامه در وضعیت «در انتظار تأیید» نیست.");
        }

        policy.Status = PolicyStatus.Active;
        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Backfills the insurer's commission rate for a policy issued before it was known —
    /// docs/TASKS.md Task 15's P&amp;L income line depends on this being set.</summary>
    [HttpPut("{id:guid}/agency-commission")]
    public async Task<ActionResult> SetAgencyCommission(Guid id, [FromBody] decimal agencyCommissionPercent, CancellationToken ct)
    {
        if (agencyCommissionPercent <= 0)
        {
            return ValidationProblem("درصد کارمزد باید مثبت باشد.");
        }

        var policy = await dbContext.Policies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null)
        {
            return NotFound();
        }

        policy.AgencyCommissionPercent = agencyCommissionPercent;
        policy.AgencyCommissionAmount = policy.NetPremium * agencyCommissionPercent / 100m;
        await dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Backs فهرست بیمه‌نامه‌ها / بیمه‌نامه‌های اقساطی / باطل‌شده‌ها — one filtered list,
    /// same shape the CollateralPage payload pattern already established for one page serving
    /// several nav items.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.PolicyRead)]
    public async Task<ActionResult<IReadOnlyList<PolicyListItemDto>>> List(
        [FromQuery] string? search, [FromQuery] string? status, [FromQuery] bool? isInstallment, CancellationToken ct)
    {
        var query = dbContext.Policies.AsNoTracking().Include(p => p.InsuranceLine).Include(p => p.Customer).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.PolicyNumber.Contains(term) || p.Customer.FullName.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PolicyStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(p => p.Status == parsedStatus);
        }

        if (isInstallment is { } flag)
        {
            query = query.Where(p => p.IsInstallment == flag);
        }

        var policies = await query.OrderByDescending(p => p.IssueDate).Take(300).ToListAsync(ct);
        var policyIds = policies.Select(p => p.Id).ToList();

        var balanceByPolicy = await dbContext.Installments.AsNoTracking()
            .Where(i => policyIds.Contains(i.PolicyId))
            .GroupBy(i => i.PolicyId)
            .Select(g => new { PolicyId = g.Key, Balance = g.Sum(i => i.Amount - i.PaidAmount) })
            .ToDictionaryAsync(g => g.PolicyId, g => g.Balance, ct);

        var result = policies
            .Select(p => new PolicyListItemDto(
                p.Id, p.PolicyNumber, p.Customer.FullName, p.InsuranceLine.NameFa, p.Status.ToString(),
                p.IsInstallment, p.TotalReceivable, balanceByPolicy.GetValueOrDefault(p.Id), p.IssueDate))
            .ToList();

        return Ok(result);
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
            var deadline = await DueDateCalculator.CalculateSettlementDeadlineAsync(dueDate, deadlineDays, shiftOnHoliday, holidayChecker, ct);

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

        // docs/TASKS.md Task 13 — generation happens here, not at issuance, because it needs the
        // actual per-installment amounts the schedule just produced. Only runs when a marketer and
        // a locked-in rate were captured at issuance (Task 6); otherwise this policy simply has no
        // commission entries, same as before Task 13 existed.
        if (policy.MarketerId is { } marketerId && policy.MarketerRatePercent is { } ratePercent)
        {
            var slices = CommissionGenerator.Generate(
                policy.NetPremium, ratePercent, policy.TotalReceivable, downPayment,
                installments.Select(i => (i.Id, i.Amount)).ToList());

            var now = DateTimeOffset.UtcNow;
            foreach (var slice in slices)
            {
                dbContext.CommissionEntries.Add(new CommissionEntry
                {
                    AgencyId = policy.AgencyId,
                    MarketerId = marketerId,
                    PolicyId = policy.Id,
                    InstallmentId = slice.InstallmentId,
                    BasePortion = slice.BasePortion,
                    RatePercent = ratePercent,
                    Amount = slice.Amount,
                    Status = slice.PayableImmediately ? CommissionStatus.Payable : CommissionStatus.Pending,
                    EligibleAt = slice.PayableImmediately ? now : null,
                });
            }

            await dbContext.SaveChangesAsync(ct);
        }

        var dto = new ScheduleResultDto(
            policy.Id,
            installmentCount > maxInstallments,
            installments.Select(i => new InstallmentDto(i.SeqNo, i.DueDate, i.Amount)).ToList());

        return (dto, null);
    }

    /// <summary>
    /// docs/TASKS.md Task 17 — the policy file: installments, endorsements, commission, and a
    /// history tab that is a single indexed query on AuditEntry.PolicyId (CLAUDE.md rule 28).
    /// </summary>
    [HttpGet("{id:guid}/file")]
    [Authorize(Policy = Permissions.PolicyRead)]
    public async Task<ActionResult<PolicyFileDto>> File(Guid id, CancellationToken ct)
    {
        var policy = await dbContext.Policies.AsNoTracking()
            .Include(p => p.InsuranceLine)
            .Include(p => p.Customer)
            .Include(p => p.Marketer)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null)
        {
            return NotFound();
        }

        var installments = await dbContext.Installments.AsNoTracking()
            .Where(i => i.PolicyId == id)
            .OrderBy(i => i.SeqNo)
            .Select(i => new PolicyInstallmentDto(i.Id, i.SeqNo, i.DueDate, i.SettlementDeadline, i.Amount, i.PaidAmount, i.Balance, i.Status.ToString()))
            .ToListAsync(ct);

        var endorsements = await dbContext.Endorsements.AsNoTracking()
            .Where(e => e.PolicyId == id)
            .OrderByDescending(e => e.IssueDate)
            .Select(e => new PolicyEndorsementDto(e.Id, e.EndorsementNo, e.Type, e.IssueDate, e.PremiumDelta, e.ServiceFeeDelta, e.Description))
            .ToListAsync(ct);

        var commissions = await dbContext.CommissionEntries.AsNoTracking()
            .Where(c => c.PolicyId == id)
            .Include(c => c.Installment)
            .OrderByDescending(c => c.EligibleAt)
            .Select(c => new PolicyCommissionRowDto(c.Id, c.Installment != null ? c.Installment.SeqNo : null, c.Amount, c.Status.ToString(), c.EligibleAt, c.PaidAt))
            .ToListAsync(ct);

        var timeline = await dbContext.AuditEntries.AsNoTracking()
            .Where(a => a.PolicyId == id)
            .OrderByDescending(a => a.OccurredAt)
            .Take(200)
            .Select(a => new TimelineEntryDto(a.UserDisplayName, a.OccurredAt, a.Description))
            .ToListAsync(ct);

        return Ok(new PolicyFileDto(
            policy.Id, policy.PolicyNumber, policy.CustomerId, policy.Customer.FullName, policy.InsuranceLine.NameFa, policy.Status.ToString(),
            policy.NetPremium, policy.ServiceFee, policy.TotalReceivable, policy.DownPayment, policy.Marketer?.FullName,
            installments, endorsements, commissions, timeline));
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
