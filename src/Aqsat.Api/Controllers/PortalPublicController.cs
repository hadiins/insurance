using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Portal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aqsat.Api.Controllers;

/// <summary>
/// The customer-facing half of the portal (docs/CUSTOMER-PORTAL-SPEC.md §3): entirely anonymous —
/// the 43-char CSPRNG token in the URL is the credential. ScopeResolutionMiddleware passes
/// unauthenticated requests through untouched; the service resolves the agency via the
/// RLS-exempt PortalInvitationTokenIndex and works RLS-scoped from there. Rate-limited per IP —
/// the only attack surface an unauthenticated caller has is token guessing.
/// </summary>
[ApiController]
[Route("api/portal")]
[AllowAnonymous]
[EnableRateLimiting("portal")]
public sealed class PortalPublicController(PortalInvitationService portalService) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<ActionResult<PublicPortalInfoDto>> GetInfo(string token, CancellationToken ct)
    {
        try
        {
            var lookup = await portalService.GetByTokenAsync(token, ct);
            var displayName = await portalService.GetCustomerDisplayNameAsync(lookup.AgencyId, lookup.Invitation.CustomerId, ct);
            return Ok(new PublicPortalInfoDto(
                displayName,
                lookup.Invitation.InquiryFeeToman,
                lookup.Invitation.Status.ToString(),
                lookup.Invitation.ExpiresAtUtc));
        }
        catch (PortalInvitationException ex)
        {
            return NotFoundProblem(ex.Message);
        }
    }

    [HttpPost("{token}/pay")]
    public async Task<ActionResult<PublicPortalPayResultDto>> Pay(string token, CancellationToken ct)
    {
        try
        {
            var result = await portalService.PayAsync(token, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            return Ok(new PublicPortalPayResultDto(result.PaidAmountToman, result.PaidAtUtc));
        }
        catch (PortalInvitationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = ex.Message,
            });
        }
    }

    private ActionResult NotFoundProblem(string message) => NotFound(new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Title = message,
    });
}
