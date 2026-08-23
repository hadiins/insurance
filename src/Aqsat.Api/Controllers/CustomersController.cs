using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
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
public sealed class CustomersController(AppDbContext dbContext, TimeProvider timeProvider) : ControllerBase
{
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
}
