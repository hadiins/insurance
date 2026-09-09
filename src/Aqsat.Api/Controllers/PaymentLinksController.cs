using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Infrastructure.Portal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aqsat.Api.Controllers;

/// <summary>
/// The operator side of the installment-payment link (/pay/{token}): status display and
/// revocation. The link itself is created and refreshed by the SMS reminder job — an operator
/// never mints one by hand. Permission is PolicyRead, matching the portal-invitation
/// controller: the link lives with the customer file.
/// </summary>
[ApiController]
[Route("api/portal/links")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class PaymentLinksController(
    InstallmentPaymentLinkService paymentLinkService,
    IScopeGuard scopeGuard,
    ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet("customer/{customerId:guid}")]
    public async Task<ActionResult<CustomerPaymentLinkDto>> GetForCustomer(Guid customerId, CancellationToken ct)
    {
        // Rule 11/17 — a foreign CustomerId must surface as not-found, not as "no link".
        try
        {
            await scopeGuard.EnsureExistsInScopeAsync<Customer>(customerId, ct);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }

        var link = await paymentLinkService.GetActiveLinkAsync(customerId, ct);
        return Ok(new CustomerPaymentLinkDto(
            link is not null, link?.Token, link?.ExpiresAtUtc, link?.LastSentAtUtc));
    }

    /// <summary>Kills the customer's active link — the token stops resolving immediately. The next
    /// reminder send creates a fresh link with a new token.</summary>
    [HttpPost("customer/{customerId:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid customerId, CancellationToken ct)
    {
        try
        {
            await scopeGuard.EnsureExistsInScopeAsync<Customer>(customerId, ct);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }

        var revoked = await paymentLinkService.RevokeAsync(
            customerId, currentUser.UserId, currentUser.DisplayName, ct);
        if (!revoked)
        {
            return Problem("لینک فعال برای این مشتری یافت نشد.");
        }

        return NoContent();
    }

    private ActionResult Problem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
