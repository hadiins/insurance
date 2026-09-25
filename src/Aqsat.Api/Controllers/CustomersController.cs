using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 17 (niaz #7) — the customer file: every policy the customer has across every
/// line, one aggregate balance, full payment history, collateral, and a unified timeline built by
/// merging AuditEntry rows across all of the customer's policies.
/// </summary>
[ApiController]
[Route("api/customers")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class CustomersController(
    AppDbContext dbContext, TimeProvider timeProvider, IFieldEncryptor fieldEncryptor,
    Aqsat.Infrastructure.Customers.CustomerCreationService customerCreationService,
    ICurrentUserContext currentUser) : ControllerBase
{
    /// <summary>The issuance wizard's step-1 entry point (owner decision 2026-08-28): everything
    /// starts with the national ID. Persian/Latin digits are normalized, the checksum is validated,
    /// then a plain equality search on the plaintext column (CLAUDE.md rule 12 rewritten — no HMAC
    /// needed to find a customer anymore). Returns Found=false for a valid-but-unknown ID so the
    /// wizard can offer inline new-customer registration instead of an error.</summary>
    [HttpGet("lookup")]
    public async Task<ActionResult<CustomerLookupResultDto>> Lookup(
        [FromQuery] string? nationalId, [FromQuery] string? passportNumber, CancellationToken ct)
    {
        // Foreign nationals (owner decision 2026-09-21): passport-only customers are looked up by
        // their passport instead. Exactly one key is accepted — an empty key is an explicit 400,
        // never a silent full-list (rule 17).
        if (!string.IsNullOrWhiteSpace(passportNumber))
        {
            var normalizedPassport = Aqsat.Infrastructure.Customers.CustomerCreationService.NormalizePassport(passportNumber);
            if (!NationalIdValidator.IsPassport(normalizedPassport))
            {
                return ValidationProblem("شمارهٔ پاسپورت نامعتبر است.");
            }

            var passportCustomer = await dbContext.Customers.AsNoTracking()
                .Where(c => c.PassportNumber == normalizedPassport)
                .Select(c => new CustomerLookupProfileDto(
                    c.Id, c.FullName, c.FirstName, c.LastName, c.NationalId,
                    c.Mobile, c.EmergencyMobile, c.Address, c.PostalCode, c.IsProfileComplete,
                    c.Kind, c.PassportNumber, c.PassportExpiry))
                .FirstOrDefaultAsync(ct);

            if (passportCustomer is null)
            {
                return Ok(new CustomerLookupResultDto(false, null, 0));
            }

            var passportPolicyCount = await dbContext.Policies.AsNoTracking()
                .CountAsync(p => p.CustomerId == passportCustomer.Id, ct);

            return Ok(new CustomerLookupResultDto(true, passportCustomer, passportPolicyCount));
        }

        var normalized = DigitNormalizer.ToLatin(nationalId ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return ValidationProblem("کد ملی یا شمارهٔ پاسپورت الزامی است.");
        }

        // Valid = Iranian OR 996-series: both share the NationalId column and lookup path.
        if (!NationalIdValidator.IsValid(normalized))
        {
            return ValidationProblem("کد ملی نامعتبر است.");
        }

        var customer = await dbContext.Customers.AsNoTracking()
            .Where(c => c.NationalId == normalized)
            .Select(c => new CustomerLookupProfileDto(
                c.Id, c.FullName, c.FirstName, c.LastName, c.NationalId,
                c.Mobile, c.EmergencyMobile, c.Address, c.PostalCode, c.IsProfileComplete,
                c.Kind, c.PassportNumber, c.PassportExpiry))
            .FirstOrDefaultAsync(ct);

        if (customer is null)
        {
            return Ok(new CustomerLookupResultDto(false, null, 0));
        }

        var policyCount = await dbContext.Policies.AsNoTracking()
            .CountAsync(p => p.CustomerId == customer.Id, ct);

        return Ok(new CustomerLookupResultDto(true, customer, policyCount));
    }

    /// <summary>Registers a brand-new customer BEFORE any policy exists, so a pre-issuance
    /// credit-check portal link can be sent on the very first visit (owner decision 2026-09-03).
    /// Returns the same shape as the wizard's step-1 lookup so the UI can slot the new customer
    /// straight into the existing-customer path.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<ActionResult<CustomerLookupResultDto>> Create(CreateCustomerRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            return ValidationProblem("نام و نام خانوادگی الزامی است.");
        }

        if (string.IsNullOrWhiteSpace(request.Mobile))
        {
            return ValidationProblem("شمارهٔ همراه الزامی است.");
        }

        // The standalone form is stricter than the issuance path (where a null national ID still
        // means an incomplete profile): a customer registered here always carries an identity
        // anchor, and Kind decides which one (owner decision 2026-09-21).
        if (request.Kind == Aqsat.Domain.Enums.CustomerKind.ForeignPassportOnly)
        {
            if (string.IsNullOrWhiteSpace(request.PassportNumber))
            {
                return ValidationProblem("برای اتباع بدون کد ملی، شمارهٔ پاسپورت الزامی است.");
            }
        }
        else if (string.IsNullOrWhiteSpace(request.NationalId))
        {
            return ValidationProblem("کد ملی الزامی است.");
        }

        Domain.Customer customer;
        try
        {
            customer = await customerCreationService.CreateAsync(
                currentUser.ActiveOrganizationId,
                new Aqsat.Infrastructure.Customers.CreateCustomerInput(
                    $"{request.FirstName.Trim()} {request.LastName.Trim()}",
                    request.FirstName, request.LastName, request.NationalId, request.Mobile,
                    request.EmergencyMobile, request.PostalCode, request.Address,
                    request.Kind, request.PassportNumber, request.PassportExpiry),
                ct);
        }
        catch (Aqsat.Infrastructure.Customers.CustomerCreationException ex)
        {
            return ValidationProblem(ex.Message);
        }

        var profile = await dbContext.Customers.AsNoTracking()
            .Where(c => c.Id == customer.Id)
            .Select(c => new CustomerLookupProfileDto(
                c.Id, c.FullName, c.FirstName, c.LastName, c.NationalId,
                c.Mobile, c.EmergencyMobile, c.Address, c.PostalCode, c.IsProfileComplete,
                c.Kind, c.PassportNumber, c.PassportExpiry))
            .FirstAsync(ct);

        return Ok(new CustomerLookupResultDto(true, profile, 0));
    }

    /// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §3 — the completion widget's counts, one per
    /// required field (extended 2026-09-07: the old two-count shape could show total=1 with both
    /// counts at 0, hiding what was actually missing).</summary>
    [HttpGet("incomplete-summary")]
    public async Task<ActionResult<IncompleteProfileSummaryDto>> IncompleteSummary(CancellationToken ct)
    {
        var counts = await dbContext.Customers.AsNoTracking()
            .Where(c => !c.IsProfileComplete)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                WithoutMobile = g.Count(c => c.Mobile == null),
                // "Without an identifier" — either kind counts as identified (owner decision
                // 2026-09-21): a passport-only customer missing their passport still needs this count.
                WithoutNationalId = g.Count(c => c.NationalId == null && c.PassportNumber == null),
                WithoutAddress = g.Count(c => c.Address == null),
                WithoutPostalCode = g.Count(c => c.PostalCode == null),
                WithoutName = g.Count(c => c.FirstName == null || c.LastName == null),
            })
            .FirstOrDefaultAsync(ct)
            ?? new { Total = 0, WithoutMobile = 0, WithoutNationalId = 0, WithoutAddress = 0, WithoutPostalCode = 0, WithoutName = 0 };

        return Ok(new IncompleteProfileSummaryDto(
            counts.Total, counts.WithoutMobile, counts.WithoutNationalId,
            counts.WithoutAddress, counts.WithoutPostalCode, counts.WithoutName));
    }

    /// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §3 — "شبیه اکسل، نه ۱۳۷ فرم": one paginated grid,
    /// 50 rows per page, optionally narrowed to whichever single field is missing.</summary>
    [HttpGet("incomplete")]
    public async Task<ActionResult<IReadOnlyList<CustomerIncompleteRowDto>>> Incomplete(
        [FromQuery] string? filter, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
    {
        var query = dbContext.Customers.AsNoTracking().Where(c => !c.IsProfileComplete);

        query = filter switch
        {
            "no-mobile" => query.Where(c => c.Mobile == null),
            "no-national-id" => query.Where(c => c.NationalId == null && c.PassportNumber == null),
            "no-address" => query.Where(c => c.Address == null),
            "no-postal-code" => query.Where(c => c.PostalCode == null),
            "no-name" => query.Where(c => c.FirstName == null || c.LastName == null),
            _ => query,
        };

        var effectivePage = page <= 0 ? 1 : page;
        var effectivePageSize = pageSize is <= 0 or > 200 ? 50 : pageSize;

        var rows = await query
            .OrderBy(c => c.FullName)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(c => new CustomerIncompleteRowDto(
                c.Id, c.FullName, c.FirstName, c.LastName, c.NationalId != null,
                c.Mobile, c.EmergencyMobile, c.Address, c.PostalCode, c.IsProfileComplete))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §3 — every present field is validated and written;
    /// a field left out of this request is untouched, so a row can be completed across more than one
    /// save. §2's format rules apply here exactly as they do on manual customer entry.</summary>
    [HttpPut("{id:guid}/complete-profile")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<ActionResult<CustomerIncompleteRowDto>> CompleteProfile(
        Guid id, CompleteCustomerProfileRequest request, CancellationToken ct)
    {
        var customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null)
        {
            return NotFound();
        }

        if (request.Mobile is not null)
        {
            if (!MobileNumberValidator.IsValid(request.Mobile))
            {
                return ValidationProblem("شمارهٔ موبایل نامعتبر است.");
            }

            customer.Mobile = MobileNumberValidator.Normalize(request.Mobile);
        }

        if (request.EmergencyMobile is not null)
        {
            if (!MobileNumberValidator.IsValid(request.EmergencyMobile))
            {
                return ValidationProblem("شمارهٔ موبایل اضطراری نامعتبر است.");
            }

            var normalizedEmergency = MobileNumberValidator.Normalize(request.EmergencyMobile);
            var mainMobile = request.Mobile is not null ? MobileNumberValidator.Normalize(request.Mobile) : customer.Mobile;
            if (normalizedEmergency == mainMobile)
            {
                return ValidationProblem("موبایل اضطراری نباید با موبایل اصلی یکسان باشد.");
            }

            customer.EmergencyMobile = normalizedEmergency;
        }

        if (request.NationalId is not null)
        {
            // The kind is already settled on the stored row; a 996-series ID belongs to a
            // ForeignResident row, an Iranian ID to an Iranian row. A checksum-valid Iranian ID on
            // a ForeignPassportOnly row is accepted too (the resident later obtained a regular ID).
            var normalizedNationalId = DigitNormalizer.ToLatin(request.NationalId).Trim();
            var accepted = customer.Kind == CustomerKind.ForeignResident
                ? NationalIdValidator.IsForeignResident(normalizedNationalId)
                : NationalIdValidator.IsIranian(normalizedNationalId)
                    || (customer.Kind == CustomerKind.ForeignPassportOnly && NationalIdValidator.IsIranian(normalizedNationalId));
            if (!accepted)
            {
                return ValidationProblem(customer.Kind == CustomerKind.ForeignResident
                    ? "کد ملی اتباع باید ۱۰ رقم شروع‌شده با ۹۹۶ باشد."
                    : "کد ملی نامعتبر است.");
            }

            customer.NationalId = normalizedNationalId;
            customer.NationalIdHash = fieldEncryptor.Hash(normalizedNationalId);
        }

        if (request.PassportNumber is not null)
        {
            var normalizedPassport = Aqsat.Infrastructure.Customers.CustomerCreationService.NormalizePassport(request.PassportNumber);
            if (!NationalIdValidator.IsPassport(normalizedPassport))
            {
                return ValidationProblem("شمارهٔ پاسپورت نامعتبر است.");
            }

            customer.PassportNumber = normalizedPassport;
            customer.PassportNumberHash = fieldEncryptor.Hash(normalizedPassport);
        }

        if (request.PassportExpiry is not null)
        {
            customer.PassportExpiry = request.PassportExpiry;
        }

        if (request.PostalCode is not null)
        {
            var normalizedPostalCode = DigitNormalizer.ToLatin(request.PostalCode).Trim();
            if (normalizedPostalCode.Length != 10 || !normalizedPostalCode.All(char.IsAsciiDigit))
            {
                return ValidationProblem("کد پستی باید دقیقاً ۱۰ رقم باشد.");
            }

            customer.PostalCode = normalizedPostalCode;
        }

        if (request.Address is not null)
        {
            if (request.Address.Trim().Length < 10)
            {
                return ValidationProblem("آدرس باید حداقل ۱۰ کاراکتر باشد.");
            }

            customer.Address = request.Address.Trim();
        }

        if (request.FirstName is not null)
        {
            if (PersonNameValidator.IsDigitsOnly(request.FirstName))
            {
                return ValidationProblem("نام نمی‌تواند عدد باشد — کد ملی در فیلد جداگانهٔ خودش وارد می‌شود.");
            }

            customer.FirstName = request.FirstName.Trim();
        }

        if (request.LastName is not null)
        {
            if (PersonNameValidator.IsDigitsOnly(request.LastName))
            {
                return ValidationProblem("نام خانوادگی نمی‌تواند عدد باشد — کد ملی در فیلد جداگانهٔ خودش وارد می‌شود.");
            }

            customer.LastName = request.LastName.Trim();
        }

        await dbContext.SaveChangesAsync(ct);

        return Ok(new CustomerIncompleteRowDto(
            customer.Id, customer.FullName, customer.FirstName, customer.LastName, customer.NationalId != null,
            customer.Mobile, customer.EmergencyMobile, customer.Address, customer.PostalCode, customer.IsProfileComplete));
    }

    /// <summary>مشتریان پرریسک — every customer with at least one currently-overdue installment or
    /// a bounced cheque, worst first.</summary>
    [HttpGet("high-risk")]
    public async Task<ActionResult<IReadOnlyList<HighRiskCustomerDto>>> HighRisk(CancellationToken ct)
    {
        var today = IranClock.Today(timeProvider);

        var overdueByCustomer = await dbContext.Installments.AsNoTracking()
            .Where(i => i.Status != InstallmentStatus.Settled && i.SettlementDeadline < today)
            .Select(i => new { i.Policy.CustomerId, i.SettlementDeadline })
            .ToListAsync(ct);

        var bouncedByCustomer = await dbContext.Collaterals.AsNoTracking()
            .Where(c => c.Status == CollateralStatus.Bounced)
            .Select(c => c.Policy.CustomerId)
            .ToListAsync(ct);

        var overdueGroups = overdueByCustomer
            .GroupBy(x => x.CustomerId)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), MaxDays: g.Max(x => today.DayNumber - x.SettlementDeadline.DayNumber)));
        var bouncedCounts = bouncedByCustomer
            .GroupBy(x => x)
            .ToDictionary(g => g.Key, g => g.Count());

        var customerIds = overdueGroups.Keys.Union(bouncedCounts.Keys).ToList();
        if (customerIds.Count == 0)
        {
            return Ok(Array.Empty<HighRiskCustomerDto>());
        }

        var customers = await dbContext.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.FullName, c.Mobile })
            .ToListAsync(ct);

        var result = customers
            .Select(c =>
            {
                var overdue = overdueGroups.GetValueOrDefault(c.Id);
                var bounced = bouncedCounts.GetValueOrDefault(c.Id);
                return new HighRiskCustomerDto(c.Id, c.FullName, c.Mobile, overdue.Count, overdue.MaxDays, bounced);
            })
            .OrderByDescending(r => r.OverdueInstallmentCount + r.BouncedChequeCount)
            .ThenByDescending(r => r.MaxDaysOverdue)
            .ToList();

        return Ok(result);
    }


    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerListItemDto>>> List([FromQuery] string? search, CancellationToken ct)
    {
        var query = dbContext.Customers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var normalized = DigitNormalizer.ToLatin(term);
            query = query.Where(c =>
                c.FullName.Contains(term)
                || (c.Mobile != null && c.Mobile.Contains(normalized))
                || (c.NationalId != null && c.NationalId.Contains(normalized))
                || dbContext.Policies.Any(p => p.CustomerId == c.Id
                    && p.Vehicle != null
                    && p.Vehicle.PlateNormalized != null
                    && p.Vehicle.PlateNormalized.Contains(normalized)));
        }

        var rows = await query
            .OrderBy(c => c.FullName)
            .Take(100)
            .Select(c => new
            {
                c.Id, c.FullName, c.Mobile,
                PolicyCount = dbContext.Policies.Count(p => p.CustomerId == c.Id)
            })
            .ToListAsync(ct);

        var deletableIds = await FindCustomerIdsWithoutHistoryAsync(rows.Select(r => r.Id).ToArray(), ct);
        var customers = rows
            .Select(r => new CustomerListItemDto(
                r.Id, r.FullName, r.Mobile, r.PolicyCount, deletableIds.Contains(r.Id)))
            .ToList();

        return Ok(customers);
    }

    /// <summary>Physically deletes a customer only when no direct business relationship exists.
    /// Query filters are intentionally ignored for soft-deletable history so a hidden/deleted row
    /// cannot become an FK failure or an accidental deletion bypass.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);

        var customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return NotFound();

        var deletable = await FindCustomerIdsWithoutHistoryAsync([id], ct);
        if (!deletable.Contains(id))
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "این مشتری سابقه دارد و حذف فیزیکی آن مجاز نیست.",
            });
        }

        var name = customer.FullName;
        var agencyId = customer.AgencyId;
        var deletedAt = DateTimeOffset.UtcNow;
        var actorId = currentUser.UserId;
        var actorName = currentUser.DisplayName;

        // The operation itself is auditable, but AuditEntry has no Customer FK and therefore does
        // not itself make a later deletion attempt fail.
        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = agencyId, UserId = actorId, UserDisplayName = actorName,
            EntityType = nameof(Customer), EntityId = id, PolicyId = Guid.Empty,
            Action = AuditAction.Deleted, Description = $"حذف فیزیکی مشتری بدون سابقه: {name}",
            OccurredAt = deletedAt, IpAddress = CurrentRequestContext.IpAddress,
        });
        dbContext.Customers.Remove(customer);
        await dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    private async Task<HashSet<Guid>> FindCustomerIdsWithoutHistoryAsync(
        IReadOnlyCollection<Guid> customerIds, CancellationToken ct)
    {
        if (customerIds.Count == 0) return [];

        var ids = customerIds.ToHashSet();
        var blocked = new HashSet<Guid>();
        async Task AddAsync<T>(
            DbSet<T> set,
            System.Linq.Expressions.Expression<Func<T, bool>> predicate,
            System.Linq.Expressions.Expression<Func<T, Guid?>> customerIdSelector) where T : class
        {
            var found = (await set.IgnoreQueryFilters().Where(predicate)
                .Select(customerIdSelector).Where(x => x != null).Select(x => x!.Value)
                .ToListAsync(ct)).ToHashSet();
            blocked.UnionWith(found);
        }

        await AddAsync(dbContext.Policies, x => ids.Contains(x.CustomerId), x => x.CustomerId);
        await AddAsync(dbContext.Payments, x => ids.Contains(x.CustomerId), x => x.CustomerId);
        await AddAsync(dbContext.GatewayTransactions, x => x.CustomerId != null && ids.Contains(x.CustomerId.Value), x => x.CustomerId);
        await AddAsync(dbContext.CreditReports, x => ids.Contains(x.CustomerId), x => x.CustomerId);
        await AddAsync(dbContext.CustomerPortalInvitations, x => ids.Contains(x.CustomerId), x => x.CustomerId);
        await AddAsync(dbContext.CustomerPaymentLinks, x => ids.Contains(x.CustomerId), x => x.CustomerId);
        await AddAsync(dbContext.CollectionContacts, x => ids.Contains(x.CustomerId), x => x.CustomerId);
        await AddAsync(dbContext.RiskAssessments, x => ids.Contains(x.CustomerId), x => x.CustomerId);
        await AddAsync(dbContext.RiskWarnings, x => ids.Contains(x.CustomerId), x => x.CustomerId);
        await AddAsync(dbContext.ManualReviews, x => ids.Contains(x.CustomerId), x => x.CustomerId);
        await AddAsync(dbContext.CustomerCreditLimits, x => ids.Contains(x.CustomerId), x => x.CustomerId);
        await AddAsync(dbContext.RenewalWatches, x => x.CustomerId != null && ids.Contains(x.CustomerId.Value), x => x.CustomerId);
        // NetworkRiskProfile is deliberately RLS-exempt, so tenant scope is explicit here.
        await AddAsync(dbContext.NetworkRiskProfiles,
            x => x.AgencyId == currentUser.ActiveOrganizationId && ids.Contains(x.CustomerId), x => x.CustomerId);
        // Keep a direct customer-level audit as history if one is ever introduced. Current profile
        // edits are not audited, so a newly registered customer remains deletable.
        blocked.UnionWith((await dbContext.AuditEntries
            .Where(a => a.EntityType == nameof(Customer) && ids.Contains(a.EntityId))
            .Select(a => a.EntityId).ToListAsync(ct)).ToHashSet());

        return ids.Except(blocked).ToHashSet();
    }

    [HttpGet("{id:guid}/file")]
    public async Task<ActionResult<CustomerFileDto>> File(Guid id, CancellationToken ct)
    {
        var customer = await dbContext.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null)
        {
            return NotFound();
        }

        var policies = await dbContext.Policies.AsNoTracking()
            .Where(p => p.CustomerId == id)
            .Include(p => p.InsuranceLine)
            .ToListAsync(ct);
        var policyIds = policies.Select(p => p.Id).ToList();

        var balanceByPolicy = await dbContext.Installments.AsNoTracking()
            .Where(i => policyIds.Contains(i.PolicyId))
            .GroupBy(i => i.PolicyId)
            .Select(g => new { PolicyId = g.Key, Balance = g.Sum(i => i.Amount - i.PaidAmount) })
            .ToDictionaryAsync(g => g.PolicyId, g => g.Balance, ct);

        var policySummaries = policies
            .Select(p => new CustomerPolicySummaryDto(
                p.Id, p.PolicyNumber, p.InsuranceLine.NameFa, p.Status.ToString(),
                p.TotalReceivable, balanceByPolicy.GetValueOrDefault(p.Id)))
            .OrderByDescending(p => p.Balance)
            .ToList();

        var payments = await dbContext.Payments.AsNoTracking()
            .Where(p => p.CustomerId == id)
            .Include(p => p.Allocations).ThenInclude(a => a.Installment).ThenInclude(i => i.Policy)
            .OrderByDescending(p => p.PaidOn)
            .ToListAsync(ct);
        var paymentDtos = payments
            .Select(p => new CustomerPaymentDto(
                p.Id, p.Amount, p.PaidOn, p.Method, p.ReferenceNo,
                p.Allocations.Select(a => $"{a.Installment.Policy.PolicyNumber} — قسط {a.Installment.SeqNo}").ToList()))
            .ToList();

        var collateral = await dbContext.Collaterals.AsNoTracking()
            .Where(c => policyIds.Contains(c.PolicyId))
            .Include(c => c.Policy)
            .OrderByDescending(c => c.DueDate)
            .Select(c => new CustomerCollateralDto(c.Id, c.Policy.PolicyNumber, c.Type.ToString(), c.Amount, c.DueDate, c.Status.ToString()))
            .ToListAsync(ct);

        var timeline = await dbContext.AuditEntries.AsNoTracking()
            .Where(a => policyIds.Contains(a.PolicyId))
            .OrderByDescending(a => a.OccurredAt)
            .Take(200)
            .Select(a => new TimelineEntryDto(a.UserDisplayName, a.OccurredAt, a.Description))
            .ToListAsync(ct);

        return Ok(new CustomerFileDto(
            customer.Id, customer.FullName, customer.Mobile, customer.NationalId,
            policySummaries.Sum(p => p.Balance),
            policySummaries, paymentDtos, collateral, timeline,
            customer.Kind, customer.PassportNumber));
    }

    [HttpGet("{id:guid}/open-installments")]
    public async Task<ActionResult<IReadOnlyList<OpenInstallmentDto>>> OpenInstallments(Guid id, CancellationToken ct)
    {
        var customerExists = await dbContext.Customers.AsNoTracking().AnyAsync(c => c.Id == id, ct);
        if (!customerExists)
        {
            return NotFound();
        }

        var policyIds = await dbContext.Policies.AsNoTracking()
            .Where(p => p.CustomerId == id)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var openInstallments = await dbContext.Installments.AsNoTracking()
            .Where(i => policyIds.Contains(i.PolicyId) && i.Status != InstallmentStatus.Settled)
            .Include(i => i.Policy).ThenInclude(p => p.InsuranceLine)
            .OrderBy(i => i.DueDate)
            .Select(i => new OpenInstallmentDto(
                i.Id, i.Policy.PolicyNumber, i.Policy.InsuranceLine.NameFa, i.SeqNo, i.DueDate, i.Balance, i.Status.ToString()))
            .ToListAsync(ct);

        return Ok(openInstallments);
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
