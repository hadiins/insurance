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
/// فهرست واحد چک‌ها — Cheques live in two tables with different jobs: Collateral (guarantee
/// cheques, never meant to be cashed) and PaymentCheque (received toward a payment). This view
/// merges both so nothing recorded anywhere hides from «چک‌های پیشِ رو» / «چک‌های برگشتی».
/// Status updates are NOT re-implemented here — the row's Source routes to the existing
/// Collateral/PaymentCheques status endpoints, keeping PaymentCheque's bounce-driven reversal
/// (PaymentReversalService) the single unwind path.
/// </summary>
[ApiController]
[Route("api/cheques")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class ChequesController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UnifiedChequeRowDto>>> List(
        [FromQuery] string? status, [FromQuery] int? upcomingDays, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly? horizon = upcomingDays is { } days ? today.AddDays(days) : null;

        CollateralStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<CollateralStatus>(status, ignoreCase: true, out var parsed))
        {
            parsedStatus = parsed;
        }

        var collateralRows = await dbContext.Collaterals.AsNoTracking()
            .Where(c => c.Type == CollateralType.ChequeSayadi)
            .Where(c => parsedStatus == null || c.Status == parsedStatus)
            .Where(c => horizon == null
                || (c.DueDate != null && c.DueDate >= today && c.DueDate <= horizon
                    && c.Status != CollateralStatus.Cleared && c.Status != CollateralStatus.Bounced))
            .OrderBy(c => c.DueDate)
            .Select(c => new UnifiedChequeRowDto(
                "Collateral", c.Id, c.PolicyId, c.Policy.PolicyNumber, c.Policy.Customer.FullName,
                null, c.SayadId, c.BankName ?? "", c.Amount, c.DueDate, c.Status.ToString(), null, null))
            .ToListAsync(ct);

        // IgnoreQueryFilters: a bounced payment cheque reverses its Payment, and the global
        // soft-delete filter would otherwise drop the row through the Payment join — hiding the
        // cheque from «چک‌های برگشتی», the very view that must show it. RLS still isolates
        // agencies (it is DB-side, unaffected); only the PaymentCheque row's own soft-delete is
        // re-applied explicitly below.
        var paymentRows = await dbContext.PaymentCheques.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => !c.IsDeleted)
            .Where(c => c.AgencyId == currentUser.ActiveOrganizationId)
            .Where(c => parsedStatus == null || c.Status == parsedStatus)
            .Where(c => horizon == null
                || (c.DueDate >= today && c.DueDate <= horizon
                    && c.Status != CollateralStatus.Cleared && c.Status != CollateralStatus.Bounced))
            .OrderBy(c => c.DueDate)
            .Select(c => new UnifiedChequeRowDto(
                "Payment", c.Id, c.PolicyId, c.Policy.PolicyNumber, c.Payment.Customer.FullName,
                c.ChequeNumber, null, c.BankName, c.Payment.Amount, c.DueDate, c.Status.ToString(),
                c.PresenterName, c.CashBox.Name))
            .ToListAsync(ct);

        var rows = collateralRows.Concat(paymentRows)
            .OrderBy(r => r.DueDate ?? DateOnly.MaxValue)
            .ToList();

        return Ok(rows);
    }
}
