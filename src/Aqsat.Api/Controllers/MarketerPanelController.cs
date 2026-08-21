using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 13 — the restricted marketer panel, docs/PHASE-1-SPEC.md §2.2's hard limits:
/// only customers this marketer introduced, name only (no national ID/plate/address), overdue
/// *status* only (never amounts), and no export anywhere (this is a JSON API with no xlsx endpoint
/// here — the frontend never gets an export button to wire up). Every view is audited.
/// </summary>
[ApiController]
[Route("api/marketer-panel")]
[Authorize(Policy = Permissions.MarketerSelfView)]
public sealed class MarketerPanelController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<MarketerDto>> Me(CancellationToken ct)
    {
        var marketer = await ResolveCurrentMarketerAsync(ct);
        if (marketer is null)
        {
            return Forbid();
        }

        return Ok(new MarketerDto(marketer.Id, marketer.FullName, marketer.Mobile, marketer.Type.ToString(), marketer.IsActive, marketer.AppUserId));
    }

    [HttpGet("customers")]
    public async Task<ActionResult<IReadOnlyList<MarketerCustomerDto>>> Customers(CancellationToken ct)
    {
        var marketer = await ResolveCurrentMarketerAsync(ct);
        if (marketer is null)
        {
            return Forbid();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var policies = await dbContext.Policies
            .AsNoTracking()
            .Where(p => p.MarketerId == marketer.Id)
            .Include(p => p.Customer)
            .ToListAsync(ct);

        var overduePolicyIds = await dbContext.Installments
            .AsNoTracking()
            .Where(i => i.Status != InstallmentStatus.Settled && i.SettlementDeadline < today
                && policies.Select(p => p.Id).Contains(i.PolicyId))
            .Select(i => i.PolicyId)
            .Distinct()
            .ToListAsync(ct);

        var occurredAt = DateTimeOffset.UtcNow;
        var byCustomer = policies.GroupBy(p => p.Customer);
        var result = new List<MarketerCustomerDto>();

        foreach (var group in byCustomer)
        {
            var isOverdue = group.Any(p => overduePolicyIds.Contains(p.Id));
            result.Add(new MarketerCustomerDto(group.Key.Id, group.Key.FullName, isOverdue));

            // docs/PHASE-1-SPEC.md §2.2: "every marketer view is audited" — one row per policy this
            // view surfaced, so the agency owner's per-file history (AuditEntry.PolicyId) shows it.
            foreach (var policy in group)
            {
                dbContext.AuditEntries.Add(new AuditEntry
                {
                    AgencyId = marketer.AgencyId,
                    UserId = currentUser.UserId,
                    UserDisplayName = currentUser.DisplayName,
                    EntityType = nameof(Customer),
                    EntityId = group.Key.Id,
                    PolicyId = policy.Id,
                    Action = AuditAction.Viewed,
                    Description = $"بازاریاب {marketer.FullName} پروندهٔ {group.Key.FullName} را مشاهده کرد",
                    OccurredAt = occurredAt,
                    IpAddress = CurrentRequestContext.IpAddress,
                });
            }
        }

        await dbContext.SaveChangesAsync(ct);
        return Ok(result);
    }

    [HttpGet("commissions")]
    public async Task<ActionResult<CommissionSummaryDto>> Commissions(CancellationToken ct)
    {
        var marketer = await ResolveCurrentMarketerAsync(ct);
        if (marketer is null)
        {
            return Forbid();
        }

        var entries = await dbContext.CommissionEntries
            .AsNoTracking()
            .Where(c => c.MarketerId == marketer.Id)
            .Include(c => c.Policy)
            .Include(c => c.Installment)
            .OrderByDescending(c => c.EligibleAt)
            .ToListAsync(ct);

        var dtos = entries
            .Select(c => new CommissionEntryDto(
                c.Id, c.PolicyId, c.Policy.PolicyNumber, c.Installment?.SeqNo, c.Amount, c.Status.ToString(), c.EligibleAt, c.PaidAt))
            .ToList();

        return Ok(new CommissionSummaryDto(
            entries.Where(c => c.Status == CommissionStatus.Pending).Sum(c => c.Amount),
            entries.Where(c => c.Status == CommissionStatus.Payable).Sum(c => c.Amount),
            entries.Where(c => c.Status == CommissionStatus.Paid).Sum(c => c.Amount),
            dtos));
    }

    private async Task<Marketer?> ResolveCurrentMarketerAsync(CancellationToken ct) =>
        await dbContext.Marketers.FirstOrDefaultAsync(m => m.AppUserId == currentUser.UserId, ct);
}
