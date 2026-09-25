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
/// The counts behind the sidebar's badges. Its own endpoint rather than a field on /api/today
/// because the sidebar renders on every page — a badge must not drag the dashboard's whole payload
/// along with it, and a page that is not the dashboard still needs them.
///
/// Only counts that are a call to action get a badge: work that is open, overdue or waiting on a
/// human. Each is the same predicate the page it points at uses, so the badge can never disagree
/// with the list it opens.
/// </summary>
[ApiController]
[Route("api/nav")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class NavBadgesController(AppDbContext dbContext, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("badges")]
    public async Task<ActionResult<NavBadgesDto>> Badges(CancellationToken ct)
    {
        var today = IranClock.Today(timeProvider);

        // Overdue, not "every open installment": a worklist where most rows are not yet due would
        // show a five-figure badge permanently and stop meaning anything.
        var installmentWorklist = await dbContext.Installments.AsNoTracking()
            .CountAsync(i => i.Status != InstallmentStatus.Settled && i.SettlementDeadline < today, ct);

        var cheques = await dbContext.PaymentCheques.AsNoTracking()
            .CountAsync(c => c.Status == CollateralStatus.Held || c.Status == CollateralStatus.AtBank, ct);

        var riskReviews = await dbContext.ManualReviews.AsNoTracking()
            .CountAsync(r => r.Status == ManualReviewStatus.Pending || r.Status == ManualReviewStatus.InReview, ct);

        // The same window the bell in CountdownController.Summary uses, so the badge and the
        // notification can never tell the user two different numbers.
        var renewalWatches = await dbContext.RenewalWatches.AsNoTracking()
            .CountAsync(w => w.Status == RenewalWatchStatus.Watching
                && w.CurrentExpiryDate <= today.AddDays(w.NotifyDaysBefore), ct);

        return Ok(new NavBadgesDto(installmentWorklist, cheques, riskReviews, renewalWatches));
    }
}
