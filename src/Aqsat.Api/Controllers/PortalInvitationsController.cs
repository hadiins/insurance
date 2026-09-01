using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Infrastructure.Portal;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// The operator side of the customer portal (docs/CUSTOMER-PORTAL-SPEC.md §3): issue a TTL-limited
/// link for a customer (SMS'd automatically) and watch its status. Permission is PolicyRead — the
/// customer file lives with policy work, and Phase-1's permission list (docs/PHASE-1-SPEC.md §2)
/// is fixed.
/// </summary>
[ApiController]
[Route("api/portal/invitations")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class PortalInvitationsController(
    PortalInvitationService portalService, ICurrentUserContext currentUser, AppDbContext dbContext) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PortalInvitationDto>> Create(CreatePortalInvitationRequest request, CancellationToken ct)
    {
        PortalInvitationResult result;
        try
        {
            result = await portalService.CreateForCustomerAsync(request.CustomerId, currentUser.UserId, ct);
        }
        catch (PortalInvitationException ex)
        {
            return Problem(ex.Message);
        }

        return CreatedAtAction(
            nameof(ListForCustomer),
            new { customerId = result.Invitation.CustomerId },
            ToDto(result.Invitation, result.SmsSent));
    }

    [HttpGet("customer/{customerId:guid}")]
    public async Task<ActionResult<IReadOnlyList<PortalInvitationDto>>> ListForCustomer(
        Guid customerId, CancellationToken ct)
    {
        var invitations = await dbContext.CustomerPortalInvitations.AsNoTracking()
            .Where(i => i.CustomerId == customerId)
            .OrderByDescending(i => i.BizId)
            .Take(50)
            .ToListAsync(ct);

        // SmsSent is a creation-time fact, not persisted state — history rows report it as unknown
        // (null-shaped as false is misleading, so the DTO contract keeps it true only on create).
        return Ok(invitations.Select(i => ToDto(i, smsSent: null)).ToList());
    }

    private static PortalInvitationDto ToDto(CustomerPortalInvitation i, bool? smsSent) =>
        new(i.Id, i.CustomerId, i.Token, i.CreatedAtUtc, i.ExpiresAtUtc, i.Status.ToString(), i.InquiryFeeToman, smsSent ?? false);

    private ActionResult Problem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
