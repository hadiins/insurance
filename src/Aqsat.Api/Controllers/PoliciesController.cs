using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Commission;
using Aqsat.Application.Common;
using Aqsat.Application.Numbering;
using Aqsat.Application.Schedule;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Numbering;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
    AppDbContext dbContext, IHolidayChecker holidayChecker, ICurrentUserContext currentUser,
    IFieldEncryptor fieldEncryptor, PolicyNumberSuggestionService numberSuggestionService)
    : ControllerBase
{
    /// <summary>The marker ReportsController's cash-basis P&amp;L filters on to recognize a
    /// down-payment Payment (which carries no PaymentAllocation rows, since it isn't collected
    /// against any specific installment).</summary>
    public const string DownPaymentMethod = "پیش‌پرداخت";

    /// <summary>docs/TASK-24-POLICY-NUMBER.md §2/§3 — backs the issuance form's locked
    /// line/agency/year display and its serial suggestion.</summary>
    [HttpGet("number-suggestion")]
    public async Task<ActionResult<PolicyNumberSuggestionDto>> NumberSuggestion(
        [FromQuery] Guid insuranceLineId, [FromQuery] DateOnly issueDate, CancellationToken ct)
    {
        var suggestion = await numberSuggestionService.GetSuggestionAsync(
            currentUser.ActiveOrganizationId, insuranceLineId, issueDate, ct);

        return Ok(new PolicyNumberSuggestionDto(
            suggestion.InsurerName, suggestion.Separator, suggestion.LineCode, suggestion.AgencyCode,
            suggestion.YearDisplay, suggestion.SerialLength, suggestion.SuggestedSerial,
            suggestion.LastSerial, suggestion.LastIssueDate, suggestion.ComposedPreview, suggestion.CanCompose));
    }

    /// <summary>docs/TASK-24-POLICY-NUMBER.md §6 — non-blocking cross-checks for the "ورود دستی
    /// شمارهٔ کامل" escape hatch only (the structured form's segments can't disagree by
    /// construction). Always 200 — a mismatch is a warning to confirm, never an error.</summary>
    [HttpGet("number-warnings")]
    public async Task<ActionResult<PolicyNumberWarningsDto>> NumberWarnings(
        [FromQuery] string policyNumber, [FromQuery] Guid insuranceLineId, [FromQuery] DateOnly issueDate, CancellationToken ct)
    {
        var warnings = await numberSuggestionService.CheckWarningsAsync(
            currentUser.ActiveOrganizationId, policyNumber, insuranceLineId, issueDate, ct);
        return Ok(new PolicyNumberWarningsDto(warnings));
    }

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

        // docs/TASK-24-POLICY-NUMBER.md §4.4 — PolicyNumber is the source of truth and is "هرگز
        // بازنویسی نمی‌شود" (never rewritten); only trimmed, never separator-normalized. Normalize()
        // is applied separately below, only to derive the parsed Pn* search/report columns.
        var policyNumber = request.PolicyNumber.Trim();

        // docs/TASK-24-POLICY-NUMBER.md §6 — "تنها خطای مسدودکننده: شمارهٔ تکراری در همان
        // نمایندگی" — the unique index would also catch this, but only as a raw SQL exception with
        // no link to the existing policy for the agent to go look at.
        var duplicate = await dbContext.Policies.AsNoTracking()
            .Where(p => p.PolicyNumber == policyNumber)
            .Select(p => new { p.Id })
            .FirstOrDefaultAsync(ct);
        if (duplicate is not null)
        {
            return ValidationProblem($"این شماره قبلاً برای بیمه‌نامهٔ دیگری ثبت شده است: {duplicate.Id}");
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
                FirstName = string.IsNullOrWhiteSpace(request.CustomerFirstName) ? null : request.CustomerFirstName.Trim(),
                LastName = string.IsNullOrWhiteSpace(request.CustomerLastName) ? null : request.CustomerLastName.Trim(),
            };

            if (!string.IsNullOrWhiteSpace(request.CustomerMobile))
            {
                if (!MobileNumberValidator.IsValid(request.CustomerMobile))
                {
                    return ValidationProblem("شمارهٔ موبایل بیمه‌گذار نامعتبر است.");
                }

                customer.Mobile = MobileNumberValidator.Normalize(request.CustomerMobile);
            }

            if (!string.IsNullOrWhiteSpace(request.CustomerEmergencyMobile))
            {
                if (!MobileNumberValidator.IsValid(request.CustomerEmergencyMobile))
                {
                    return ValidationProblem("شمارهٔ موبایل اضطراری نامعتبر است.");
                }

                var normalizedEmergency = MobileNumberValidator.Normalize(request.CustomerEmergencyMobile);
                if (normalizedEmergency == customer.Mobile)
                {
                    return ValidationProblem("موبایل اضطراری نباید با موبایل اصلی یکسان باشد.");
                }

                customer.EmergencyMobile = normalizedEmergency;
            }

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

            if (!string.IsNullOrWhiteSpace(request.CustomerPostalCode))
            {
                var normalizedPostalCode = DigitNormalizer.ToLatin(request.CustomerPostalCode).Trim();
                if (normalizedPostalCode.Length != 10 || !normalizedPostalCode.All(char.IsAsciiDigit))
                {
                    return ValidationProblem("کد پستی باید دقیقاً ۱۰ رقم باشد.");
                }

                customer.PostalCode = normalizedPostalCode;
            }

            if (!string.IsNullOrWhiteSpace(request.CustomerAddress))
            {
                if (request.CustomerAddress.Trim().Length < 10)
                {
                    return ValidationProblem("آدرس بیمه‌گذار باید حداقل ۱۰ کاراکتر باشد.");
                }

                customer.Address = request.CustomerAddress.Trim();
            }

            dbContext.Customers.Add(customer);
            await dbContext.SaveChangesAsync(ct);
            customerId = customer.Id;
        }

        Guid? vehicleId = null;
        if (!IsEmptyVehicle(request.Vehicle))
        {
            var v = request.Vehicle!;
            var hasStructuredPlate = !string.IsNullOrWhiteSpace(v.PlateTwoDigit) && !string.IsNullOrWhiteSpace(v.PlateLetter)
                && !string.IsNullOrWhiteSpace(v.PlateThreeDigit) && !string.IsNullOrWhiteSpace(v.PlateIranCode);

            var vehicle = new Vehicle
            {
                AgencyId = agencyId,
                // docs/TASK-25-IDENTITY-VEHICLE.md §5.3 — PlateNormalized is always derived
                // server-side from the four structured parts, never trusted as a client-sent string.
                Plate = hasStructuredPlate
                    ? PlateParser.Compose(v.PlateTwoDigit!, v.PlateLetter!, v.PlateThreeDigit!, v.PlateIranCode!)
                    : v.Plate,
                Vin = v.Vin,
                Chassis = v.Chassis,
                Make = v.Make,
                Model = v.Model,
                Year = v.Year,
                PlateType = v.PlateType is { } pt ? (PlateType)pt : PlateType.Personal,
                PlateTwoDigit = hasStructuredPlate ? DigitNormalizer.ToLatin(v.PlateTwoDigit!) : null,
                PlateLetter = hasStructuredPlate ? v.PlateLetter : null,
                PlateThreeDigit = hasStructuredPlate ? DigitNormalizer.ToLatin(v.PlateThreeDigit!) : null,
                PlateIranCode = hasStructuredPlate ? DigitNormalizer.ToLatin(v.PlateIranCode!) : null,
            };
            vehicle.PlateNormalized = hasStructuredPlate ? vehicle.Plate : null;
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

        // The agency's own commission rate from the insurer is locked at issuance the same way —
        // an explicit AgencyCommissionPercent on the request always wins (manual override /
        // PUT-style backfill from before this rate table existed); otherwise auto-lookup the
        // active per-line rate, same precedence as the marketer lookup above.
        var agencyCommissionPercent = request.AgencyCommissionPercent
            ?? await dbContext.AgencyCommissionRates
                .AsNoTracking()
                .Where(r => r.InsuranceLineId == request.InsuranceLineId
                    && r.EffectiveFrom <= request.IssueDate && (r.EffectiveTo == null || r.EffectiveTo >= request.IssueDate))
                .OrderByDescending(r => r.EffectiveFrom)
                .Select(r => (decimal?)r.RatePercent)
                .FirstOrDefaultAsync(ct);

        // docs/TASK-24-POLICY-NUMBER.md §5 — parsing never blocks issuance; a failed parse just
        // leaves PnIsParsed=false with a note while PolicyNumber itself is still saved verbatim.
        // Ensures the format/line-code defaults exist even if this agency's very first action is
        // issuing a policy without ever hitting the number-suggestion endpoint first.
        await PolicyNumberDefaultsSeeder.EnsureAgencyDefaultsAsync(dbContext, agencyId, ct);
        var format = await dbContext.PolicyNumberFormats.AsNoTracking().FirstOrDefaultAsync(f => f.IsActive, ct);
        var parts = format is not null
            ? PolicyNumberParser.Parse(policyNumber, format)
            : new PolicyNumberParts(policyNumber, null, null, null, null, false, "الگوی شماره‌ای برای این نمایندگی تنظیم نشده است.");

        var policy = new Policy
        {
            AgencyId = agencyId,
            PolicyNumber = policyNumber,
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
            AgencyCommissionPercent = agencyCommissionPercent,
            AgencyCommissionAmount = agencyCommissionPercent is { } pct ? request.NetPremium * pct / 100m : null,
            PnLineCode = parts.LineCode,
            PnAgencyCode = parts.AgencyCode,
            PnYear = parts.Year,
            PnSerial = parts.Serial,
            PnIsParsed = parts.IsParsed,
            PnParseNote = parts.Note,
            PnManualEntry = request.PnManualEntry,
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

    /// <summary>Non-installment policies never generate Installment rows, so they have no
    /// per-installment settlement event to flip a commission slice payable on — this is that event,
    /// for the whole policy at once. Idempotent the same way RecordPaymentAsync is (rule 24):
    /// InstallmentIdHint = policy.Id is safe here since IsInstallment policies never reach
    /// GenerateScheduleAsync's own down-payment receipt, so there is no hint collision.</summary>
    [HttpPost("{id:guid}/record-full-payment")]
    [Authorize(Policy = Permissions.PaymentWrite)]
    public async Task<ActionResult<RecordFullPaymentResultDto>> RecordFullPayment(
        Guid id, RecordFullPaymentRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0)
        {
            return ValidationProblem("مبلغ پرداخت باید مثبت باشد.");
        }

        var policy = await dbContext.Policies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null)
        {
            return NotFound();
        }

        if (policy.IsInstallment)
        {
            return ValidationProblem("این بیمه‌نامه اقساطی است؛ از فرم ثبت پرداخت قسط استفاده کنید.");
        }

        var existing = await dbContext.Payments.AsNoTracking().FirstOrDefaultAsync(
            p => p.InstallmentIdHint == policy.Id && p.PaidOn == request.PaidOn && p.Amount == request.Amount, ct);
        if (existing is not null)
        {
            return Ok(new RecordFullPaymentResultDto(existing.Id, existing.Amount));
        }

        var occurredAt = DateTimeOffset.UtcNow;
        var payment = new Payment
        {
            AgencyId = policy.AgencyId,
            CustomerId = policy.CustomerId,
            InstallmentIdHint = policy.Id,
            Amount = request.Amount,
            PaidOn = request.PaidOn,
            Method = request.Method,
            ReferenceNo = request.ReferenceNo,
            MethodType = request.MethodType ?? PaymentMethod.Cash,
            CashBoxId = request.CashBoxId,
            BankAccountId = request.BankAccountId,
            RecordedByUserId = currentUser.UserId,
        };
        dbContext.Payments.Add(payment);

        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = policy.AgencyId,
            UserId = currentUser.UserId,
            UserDisplayName = currentUser.DisplayName,
            EntityType = nameof(Payment),
            EntityId = payment.Id,
            PolicyId = policy.Id,
            Action = AuditAction.PaymentRecorded,
            Description = $"ثبت پرداخت کامل بیمه‌نامهٔ {policy.PolicyNumber}",
            OccurredAt = occurredAt,
            IpAddress = CurrentRequestContext.IpAddress,
        });

        // The single full-policy commission slice is created lazily here rather than at issuance
        // (docs-plan default: imported policies rarely have AgencyCommissionPercent set until the
        // agent backfills it via the PUT above, well after issuance) and immediately flipped
        // Payable, mirroring the down-payment slice's "money in hand = payable now" rule.
        if (policy.AgencyCommissionPercent is { } ratePercent)
        {
            var entry = await dbContext.AgencyCommissionEntries
                .FirstOrDefaultAsync(e => e.PolicyId == policy.Id && e.IsFullPolicySlice, ct);
            if (entry is null)
            {
                entry = new AgencyCommissionEntry
                {
                    AgencyId = policy.AgencyId,
                    PolicyId = policy.Id,
                    InstallmentId = null,
                    IsFullPolicySlice = true,
                    BasePortion = policy.NetPremium,
                    RatePercent = ratePercent,
                    Amount = Math.Round(policy.NetPremium * ratePercent / 100m, 0, MidpointRounding.ToEven),
                    Status = CommissionStatus.Pending,
                };
                dbContext.AgencyCommissionEntries.Add(entry);
            }

            if (entry.Status == CommissionStatus.Pending)
            {
                entry.Status = CommissionStatus.Payable;
                entry.EligibleAt = occurredAt;
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
            {
                entry.State = EntityState.Detached;
            }

            var raced = await dbContext.Payments.AsNoTracking().FirstOrDefaultAsync(
                p => p.InstallmentIdHint == policy.Id && p.PaidOn == request.PaidOn && p.Amount == request.Amount, ct);
            if (raced is not null)
            {
                return Ok(new RecordFullPaymentResultDto(raced.Id, raced.Amount));
            }

            return ValidationProblem("خطای پایگاه‌داده هنگام ثبت پرداخت.");
        }

        return Ok(new RecordFullPaymentResultDto(payment.Id, payment.Amount));
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
            var normalized = DigitNormalizer.ToLatin(term);

            // docs/TASK-24-POLICY-NUMBER.md §8 — four input shapes, OR'd together rather than
            // strictly disambiguated (a 4-digit term is genuinely ambiguous between a full "1405"
            // year and a "1110" line code per the doc's own table): full number substring match,
            // serial with-or-without leading zeros, year (3 or 4 digit), and line code.
            if (normalized.Length > 0 && normalized.All(char.IsAsciiDigit))
            {
                var paddedSerial = normalized.PadLeft(6, '0');
                int? candidateYear = normalized.Length switch
                {
                    3 => 1000 + int.Parse(normalized),
                    4 => int.Parse(normalized),
                    _ => null,
                };

                query = query.Where(p =>
                    p.PolicyNumber.Contains(term)
                    || p.Customer.FullName.Contains(term)
                    || p.PnSerial == normalized
                    || p.PnSerial == paddedSerial
                    || (candidateYear != null && p.PnYear == candidateYear)
                    || p.PnLineCode == normalized);
            }
            else
            {
                query = query.Where(p => p.PolicyNumber.Contains(term) || p.Customer.FullName.Contains(term));
            }
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
            && string.IsNullOrWhiteSpace(vehicle.Chassis) && string.IsNullOrWhiteSpace(vehicle.PlateTwoDigit));

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

    /// <summary>Stage 4/7 — the actual money-in-hand event for a scheduled policy's down payment,
    /// decoupled from /schedule itself (which only sizes the installments). Flips the down-payment
    /// slice of both CommissionEntry (marketer) and AgencyCommissionEntry Payable, mirroring
    /// RecordPaymentAsync's per-installment flip. Idempotent the same way (rule 24):
    /// InstallmentIdHint = policy.Id, unique per (AgencyId, PaidOn, Amount).</summary>
    [HttpPost("{id:guid}/receive-down-payment")]
    [Authorize(Policy = Permissions.PaymentWrite)]
    public async Task<ActionResult<ReceiveDownPaymentResultDto>> ReceiveDownPayment(
        Guid id, ReceiveDownPaymentRequest request, CancellationToken ct)
    {
        var policy = await dbContext.Policies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null)
        {
            return NotFound();
        }

        if (policy.InstallmentCount == 0)
        {
            return ValidationProblem("این بیمه‌نامه هنوز زمان‌بندی نشده است.");
        }

        if (policy.DownPayment <= 0)
        {
            return ValidationProblem("این بیمه‌نامه پیش‌پرداختی ندارد.");
        }

        var existing = await dbContext.Payments.AsNoTracking().FirstOrDefaultAsync(
            p => p.InstallmentIdHint == policy.Id && p.PaidOn == request.PaidOn && p.Amount == policy.DownPayment, ct);
        if (existing is not null)
        {
            return Ok(new ReceiveDownPaymentResultDto(existing.Id, existing.Amount));
        }

        var occurredAt = DateTimeOffset.UtcNow;
        var payment = new Payment
        {
            AgencyId = policy.AgencyId,
            CustomerId = policy.CustomerId,
            InstallmentIdHint = policy.Id,
            Amount = policy.DownPayment,
            PaidOn = request.PaidOn,
            Method = DownPaymentMethod,
            ReferenceNo = request.ReferenceNo,
            MethodType = request.MethodType ?? PaymentMethod.Cash,
            CashBoxId = request.CashBoxId,
            BankAccountId = request.BankAccountId,
            RecordedByUserId = currentUser.UserId,
        };
        dbContext.Payments.Add(payment);

        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = policy.AgencyId,
            UserId = currentUser.UserId,
            UserDisplayName = currentUser.DisplayName,
            EntityType = nameof(Payment),
            EntityId = payment.Id,
            PolicyId = policy.Id,
            Action = AuditAction.PaymentRecorded,
            Description = $"ثبت پیش‌پرداخت بیمه‌نامهٔ {policy.PolicyNumber}",
            OccurredAt = occurredAt,
            IpAddress = CurrentRequestContext.IpAddress,
        });

        var downCommissionEntry = await dbContext.CommissionEntries
            .FirstOrDefaultAsync(c => c.PolicyId == policy.Id && c.InstallmentId == null, ct);
        if (downCommissionEntry is { Status: CommissionStatus.Pending })
        {
            downCommissionEntry.Status = CommissionStatus.Payable;
            downCommissionEntry.EligibleAt = occurredAt;
        }

        var downAgencyEntry = await dbContext.AgencyCommissionEntries
            .FirstOrDefaultAsync(c => c.PolicyId == policy.Id && c.InstallmentId == null && !c.IsFullPolicySlice, ct);
        if (downAgencyEntry is { Status: CommissionStatus.Pending })
        {
            downAgencyEntry.Status = CommissionStatus.Payable;
            downAgencyEntry.EligibleAt = occurredAt;
        }

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
            {
                entry.State = EntityState.Detached;
            }

            var raced = await dbContext.Payments.AsNoTracking().FirstOrDefaultAsync(
                p => p.InstallmentIdHint == policy.Id && p.PaidOn == request.PaidOn && p.Amount == policy.DownPayment, ct);
            if (raced is not null)
            {
                return Ok(new ReceiveDownPaymentResultDto(raced.Id, raced.Amount));
            }

            return ValidationProblem("خطای پایگاه‌داده هنگام ثبت پیش‌پرداخت.");
        }

        return Ok(new ReceiveDownPaymentResultDto(payment.Id, payment.Amount));
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
        //
        // Stage 4/7 — every slice, including the down-payment one, starts Pending: scheduling no
        // longer implies the down payment was actually collected (that used to be conflated: the
        // down-payment Payment receipt was auto-created right here). Now a real
        // POST /{id}/receive-down-payment call is what flips the down-payment slice Payable, with a
        // real date/method/cashbox — CommissionGenerator's own PayableImmediately flag is ignored
        // here on purpose.
        if (policy.MarketerId is { } marketerId && policy.MarketerRatePercent is { } ratePercent)
        {
            var slices = CommissionGenerator.Generate(
                policy.NetPremium, ratePercent, policy.TotalReceivable, downPayment,
                installments.Select(i => (i.Id, i.Amount)).ToList());

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
                    Status = CommissionStatus.Pending,
                });
            }

            await dbContext.SaveChangesAsync(ct);
        }

        // Mirrors the marketer commission generation immediately above, minus the MarketerId
        // dimension — same CommissionGenerator, same rounding/proportional-slice guarantee, only
        // runs when a rate was actually locked at issuance (stage 2/7).
        if (policy.AgencyCommissionPercent is { } agencyRatePercent)
        {
            var agencySlices = CommissionGenerator.Generate(
                policy.NetPremium, agencyRatePercent, policy.TotalReceivable, downPayment,
                installments.Select(i => (i.Id, i.Amount)).ToList());

            foreach (var slice in agencySlices)
            {
                dbContext.AgencyCommissionEntries.Add(new AgencyCommissionEntry
                {
                    AgencyId = policy.AgencyId,
                    PolicyId = policy.Id,
                    InstallmentId = slice.InstallmentId,
                    IsFullPolicySlice = false,
                    BasePortion = slice.BasePortion,
                    RatePercent = agencyRatePercent,
                    Amount = slice.Amount,
                    Status = CommissionStatus.Pending,
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
