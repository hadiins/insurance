using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
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
public sealed class CustomersController(AppDbContext dbContext, TimeProvider timeProvider, IFieldEncryptor fieldEncryptor) : ControllerBase
{
    /// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §3 — the dashboard widget's two counts.</summary>
    [HttpGet("incomplete-summary")]
    public async Task<ActionResult<IncompleteProfileSummaryDto>> IncompleteSummary(CancellationToken ct)
    {
        var total = await dbContext.Customers.AsNoTracking().CountAsync(c => !c.IsProfileComplete, ct);
        var withoutMobile = await dbContext.Customers.AsNoTracking().CountAsync(c => c.Mobile == null, ct);
        var withoutNationalId = await dbContext.Customers.AsNoTracking().CountAsync(c => c.NationalId == null, ct);

        return Ok(new IncompleteProfileSummaryDto(total, withoutMobile, withoutNationalId));
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
            "no-national-id" => query.Where(c => c.NationalId == null),
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
            if (!NationalIdValidator.IsValid(request.NationalId))
            {
                return ValidationProblem("کد ملی نامعتبر است.");
            }

            var normalizedNationalId = DigitNormalizer.ToLatin(request.NationalId).Trim();
            customer.NationalId = normalizedNationalId;
            customer.NationalIdHash = fieldEncryptor.Hash(normalizedNationalId);
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
            customer.FirstName = request.FirstName.Trim();
        }

        if (request.LastName is not null)
        {
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
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

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
            query = query.Where(c => c.FullName.Contains(term) || (c.Mobile != null && c.Mobile.Contains(term)));
        }

        var customers = await query
            .OrderBy(c => c.FullName)
            .Take(100)
            .Select(c => new CustomerListItemDto(
                c.Id, c.FullName, c.Mobile, dbContext.Policies.Count(p => p.CustomerId == c.Id)))
            .ToListAsync(ct);

        return Ok(customers);
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
            customer.Id, customer.FullName, customer.Mobile, MaskNationalId(customer.NationalId),
            policySummaries.Sum(p => p.Balance),
            policySummaries, paymentDtos, collateral, timeline));
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

    /// <summary>CLAUDE.md rule 12 — national ID is decrypted in memory by the EF value converter on
    /// read, but must never reach the UI unmasked.</summary>
    private static string? MaskNationalId(string? nationalId) =>
        string.IsNullOrEmpty(nationalId) || nationalId.Length < 7
            ? nationalId
            : $"{nationalId[..4]}•••{nationalId[^3..]}";

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
