using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Portal;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// The operator side of the customer portal (docs/CUSTOMER-PORTAL-SPEC.md §3): issue a TTL-limited
/// link for a customer (SMS'd automatically), watch its status, read the customer's latest credit
/// report (standalone inquiries included), and retry a failed standalone inquiry run. Permission
/// is PolicyRead — the customer file lives with policy work, and Phase-1's permission list
/// (docs/PHASE-1-SPEC.md §2) is fixed.
/// </summary>
[ApiController]
[Route("api/portal/invitations")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class PortalInvitationsController(
    PortalInvitationService portalService,
    PolicyVerificationService verificationService,
    IScopeGuard scopeGuard,
    ICurrentUserContext currentUser,
    AppDbContext dbContext) : ControllerBase
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

    /// <summary>The customer's latest credit report, any PolicyId — standalone (customer-file
    /// portal link) reports included. Feeds the customer file's «گزارش اعتباری» card and the
    /// issuance wizard's step-1 status strip.</summary>
    [HttpGet("customer/{customerId:guid}/credit-report")]
    public async Task<ActionResult<CustomerCreditReportDto>> GetCreditReport(Guid customerId, CancellationToken ct)
    {
        // Rule 11/17 — a foreign CustomerId must surface as not-found, not as "no report".
        try
        {
            await scopeGuard.EnsureExistsInScopeAsync<Customer>(customerId, ct);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }

        var report = await verificationService.GetLatestCustomerReportAsync(customerId, ct);

        // A standalone link whose fee landed but whose inquiries failed (Status=Paid, Stage=FeePaid)
        // is what the UI's «تلاش مجدد استعلام» button targets.
        var failedStandalone = await dbContext.CustomerPortalInvitations.AsNoTracking()
            .Where(i => i.CustomerId == customerId
                && i.PolicyId == null
                && i.Stage == PolicyVerificationStage.FeePaid)
            .OrderByDescending(i => i.BizId)
            .FirstOrDefaultAsync(ct);

        return Ok(new CustomerCreditReportDto(
            report is null ? null : PolicyVerificationController.ToReportDto(report),
            report is not null && PolicyVerificationService.IsReusable(report),
            report is { RawSuccess: true } ? report.RetrievedAtUtc + PolicyVerificationService.ReportReuseWindow : null,
            failedStandalone?.Id));
    }

    /// <summary>Re-fires both api.ir inquiries for a standalone (customer-file) link whose fee
    /// landed but whose inquiry run failed — no second fee. Other-agency invitation ids are
    /// invisible under RLS and come back 404.</summary>
    [HttpPost("{invitationId:guid}/retry-inquiries")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<ActionResult<CustomerCreditReportDto>> RetryStandaloneInquiries(
        Guid invitationId, CancellationToken ct)
    {
        InquiryRunResult result;
        try
        {
            result = await verificationService.RetryStandaloneInquiriesAsync(invitationId, ct);
        }
        catch (PortalInvitationException)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            return Problem(result.Error ?? "استعلام اعتباری ناموفق بود.");
        }

        var invitation = await dbContext.CustomerPortalInvitations.AsNoTracking()
            .FirstAsync(i => i.Id == invitationId, ct);
        var report = await verificationService.GetLatestCustomerReportAsync(invitation.CustomerId, ct);
        return Ok(new CustomerCreditReportDto(
            report is null ? null : PolicyVerificationController.ToReportDto(report),
            report is not null && PolicyVerificationService.IsReusable(report),
            report is { RawSuccess: true } ? report.RetrievedAtUtc + PolicyVerificationService.ReportReuseWindow : null,
            null));
    }

    private static PortalInvitationDto ToDto(CustomerPortalInvitation i, bool? smsSent) =>
        new(i.Id, i.CustomerId, i.Token, i.CreatedAtUtc, i.ExpiresAtUtc, i.Status.ToString(), i.InquiryFeeToman,
            smsSent ?? false, i.Stage.ToString());

    private ActionResult Problem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
