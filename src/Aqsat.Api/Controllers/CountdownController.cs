using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Countdown;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 9 — the product. Query per docs/PHASE-1-SPEC.md §3.3: open installments whose
/// settlement window is active or about to be, ordered by deadline urgency. RLS confines the result
/// to the caller's agency; ScopeResolutionMiddleware guarantees AgencyContext is set and non-empty
/// before this ever runs (CLAUDE.md rule 17 — an invalid/missing scope is a 403 upstream, never a
/// silent empty list here).
/// </summary>
[ApiController]
[Route("api/countdown")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class CountdownController(AppDbContext dbContext, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CountdownDashboardDto>> Get(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var windowStart = today.AddDays(-30);
        var windowEnd = today.AddDays(7);

        var rows = await dbContext.Installments
            .AsNoTracking()
            .Where(i => i.Status != InstallmentStatus.Settled
                && i.SettlementDeadline >= windowStart
                && i.DueDate <= windowEnd)
            .OrderBy(i => i.SettlementDeadline)
            .ThenByDescending(i => i.Amount)
            .Select(i => new
            {
                i.Id,
                i.PolicyId,
                i.Policy.PolicyNumber,
                CustomerFullName = i.Policy.Customer.FullName,
                i.SeqNo,
                i.DueDate,
                i.SettlementDeadline,
                i.Amount,
                i.PaidAmount,
                i.Status,
            })
            .ToListAsync(ct);

        var dtos = rows
            .Select(r => new CountdownRowDto(
                r.Id,
                r.PolicyId,
                r.PolicyNumber,
                r.CustomerFullName,
                r.SeqNo,
                r.DueDate,
                r.SettlementDeadline,
                r.Amount,
                r.PaidAmount,
                r.Amount - r.PaidAmount,
                r.Status.ToString(),
                CountdownUrgencyClassifier.Classify(today, r.DueDate, r.SettlementDeadline).ToString()))
            .ToList();

        var owed = dtos.Sum(r => r.Amount);
        var collected = dtos.Sum(r => r.PaidAmount);

        return Ok(new CountdownDashboardDto(owed, collected, owed - collected, dtos));
    }

    /// <summary>Backs the header notification bell — the three counters it shows, in one call so
    /// the shell never fires three requests on every poll.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<TodaySummaryDto>> Summary(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var overdue = await dbContext.Installments.AsNoTracking()
            .CountAsync(i => i.Status != InstallmentStatus.Settled && i.SettlementDeadline < today, ct);

        var dueRenewals = await dbContext.RenewalWatches.AsNoTracking()
            .CountAsync(w => w.Status == RenewalWatchStatus.Watching
                && w.CurrentExpiryDate <= today.AddDays(w.NotifyDaysBefore), ct);

        var counts = await dbContext.Customers.AsNoTracking()
            .Where(c => !c.IsProfileComplete)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                WithoutMobile = g.Count(c => c.Mobile == null),
                WithoutNationalId = g.Count(c => c.NationalId == null),
                WithoutAddress = g.Count(c => c.Address == null),
                WithoutPostalCode = g.Count(c => c.PostalCode == null),
                WithoutName = g.Count(c => c.FirstName == null || c.LastName == null),
            })
            .FirstOrDefaultAsync(ct);

        return Ok(new TodaySummaryDto(overdue, dueRenewals, new IncompleteProfileSummaryDto(
            counts?.Total ?? 0, counts?.WithoutMobile ?? 0, counts?.WithoutNationalId ?? 0,
            counts?.WithoutAddress ?? 0, counts?.WithoutPostalCode ?? 0, counts?.WithoutName ?? 0)));
    }
}
