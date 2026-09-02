using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Infrastructure.Portal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aqsat.Api.Controllers;

/// <summary>
/// The operator side of the issuance-verification chain (owner decision 2026-09-01) — wizard
/// step 3.5, between scheduling and the down-payment receipt: start the chain (the customer
/// gets the portal link by SMS), watch the stage and the api.ir credit report, approve or
/// reject (reject cancels the policy), and retry a failed inquiry run. Writes need
/// PolicyWrite, the status read rides PolicyRead like every other policy-subresource endpoint.
/// </summary>
[ApiController]
[Route("api/policies/{policyId:guid}/verification")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class PolicyVerificationController(
    PolicyVerificationService verificationService, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<ActionResult<PolicyVerificationDto>> Create(CancellationToken ct)
    {
        PolicyVerificationResult result;
        try
        {
            result = await verificationService.CreateForPolicyAsync(PolicyId, currentUser.UserId, ct);
        }
        catch (PortalInvitationException ex)
        {
            return Problem(ex.Message);
        }

        return Ok(ToDto(result.Invitation, result.SmsSent));
    }

    [HttpGet]
    public async Task<ActionResult<PolicyVerificationDto>> Get(CancellationToken ct)
    {
        var (invitation, report) = await verificationService.GetStatusAsync(PolicyId, ct);
        return Ok(ToDto(invitation, smsSent: null, report));
    }

    /// <summary>Approve moves the chain to the customer's contract approval; reject is terminal
    /// and cancels the policy with it.</summary>
    [HttpPost("decision")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<ActionResult<PolicyVerificationDto>> Decide(AgencyVerificationDecisionRequest request, CancellationToken ct)
    {
        PolicyVerificationResult result;
        try
        {
            result = await verificationService.AgencyDecisionAsync(
                PolicyId, request.Approve, currentUser.UserId, currentUser.DisplayName, ct);
        }
        catch (PortalInvitationException ex)
        {
            return Problem(ex.Message);
        }

        return Ok(ToDto(result.Invitation, result.SmsSent));
    }

    /// <summary>For a paid-but-failed inquiry run (stage still FeePaid) — re-fires both api.ir
    /// credit inquiries without charging the customer again.</summary>
    [HttpPost("retry-inquiries")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<ActionResult<PolicyVerificationDto>> RetryInquiries(CancellationToken ct)
    {
        InquiryRunResult result;
        try
        {
            result = await verificationService.RetryInquiriesAsync(PolicyId, ct);
        }
        catch (PortalInvitationException ex)
        {
            return Problem(ex.Message);
        }

        var (invitation, report) = await verificationService.GetStatusAsync(PolicyId, ct);
        var dto = ToDto(invitation, smsSent: null, report);
        return result.Succeeded
            ? Ok(dto)
            : Problem(result.Error ?? "استعلام اعتباری ناموفق بود.");
    }

    private Guid PolicyId => Guid.Parse(RouteData.Values["policyId"]!.ToString()!);

    private static PolicyVerificationDto ToDto(
        CustomerPortalInvitation? i, bool? smsSent, CreditReport? report = null) =>
        new(
            i?.Id, i?.Token, i?.Stage.ToString(), i?.Status.ToString(), i?.ExpiresAtUtc,
            i?.InquiryFeeToman, i?.DownPaymentAmountToman,
            i?.AgencyDecisionAtUtc, i?.CustomerApprovedAtUtc, i?.DownPaymentPaidAtUtc,
            report is null ? null : ToDto(report), smsSent);

    private static CreditReportDto ToDto(CreditReport r) =>
        new(
            r.ChequeCount, r.ChequeSumAmountToman, r.ChequeSumBouncedAmountToman,
            r.ActiveLoansCount, r.LoanTotalAmountToman, r.LoanDebtTotalAmountToman,
            r.LoanPastExpiredTotalAmountToman, r.LoanDeferredTotalAmountToman,
            r.LoanSuspiciousTotalAmountToman, r.LoanDishonoredAmountToman,
            r.RawSuccess, r.RetrievedAtUtc);

    private ActionResult Problem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
