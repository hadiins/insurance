using Aqsat.Api.Contracts;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Portal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aqsat.Api.Controllers;

/// <summary>
/// The customer-facing half of the portal (docs/CUSTOMER-PORTAL-SPEC.md §3): entirely anonymous —
/// the 43-char CSPRNG token in the URL is the credential. ScopeResolutionMiddleware passes
/// unauthenticated requests through untouched; the services resolve the agency through the
/// RLS-exempt PortalInvitationTokenIndex and work RLS-scoped from there. Rate-limited per IP —
/// the only attack surface an unauthenticated caller has is token guessing.
///
/// For a policy-verification link the same token also drives the staged chain: GET returns the
/// current stage with its stage-gated content (contract text + installment schedule only after
/// the agency approves), approve-contract is the customer's sign-off, pay-down-payment is the
/// chain's final hop through the agency's own gateway.
///
/// Every response carries the customer's identity, balances, or policy numbers behind a bearer
/// URL — no browser, shared proxy, or intermediary cache may ever store one: no-store on the
/// whole controller.
/// </summary>
[ApiController]
[Route("api/portal")]
[AllowAnonymous]
[EnableRateLimiting("portal")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PortalPublicController(
    PortalInvitationService portalService,
    PolicyVerificationService verificationService,
    InstallmentPaymentLinkService paymentLinkService) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<ActionResult<PublicPortalInfoDto>> GetInfo(string token, CancellationToken ct)
    {
        try
        {
            var stage = await verificationService.GetPublicStageAsync(token, ct);
            var i = stage.Invitation;
            var showContract = i.PolicyId is not null
                && i.Stage is PolicyVerificationStage.AgencyApproved
                    or PolicyVerificationStage.CustomerApproved
                    or PolicyVerificationStage.Completed;
            return Ok(new PublicPortalInfoDto(
                stage.CustomerDisplayName,
                i.InquiryFeeToman,
                i.Status.ToString(),
                i.ExpiresAtUtc,
                i.PolicyId is null ? null : i.Stage.ToString(),
                showContract && stage.PolicyNumber.Length > 0 ? stage.PolicyNumber : null,
                showContract ? i.DownPaymentAmountToman : null,
                showContract ? stage.ContractText : null,
                showContract
                    ? stage.Installments.Select(x => new PublicPortalInstallmentDto(x.SeqNo, x.DueDate, x.Amount)).ToList()
                    : null));
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

    /// <summary>The customer accepts قوانین/قرارداد/اقساط on the portal — only reachable after the
    /// agency approved the credit report.</summary>
    [HttpPost("{token}/approve-contract")]
    public async Task<ActionResult<PublicPortalStageResultDto>> ApproveContract(string token, CancellationToken ct)
    {
        try
        {
            var invitation = await verificationService.CustomerApproveAsync(token, ct);
            return Ok(new PublicPortalStageResultDto(invitation.Stage.ToString()));
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

    /// <summary>The chain's final hop — the down payment, through the AGENCY's gateway.</summary>
    [HttpPost("{token}/pay-down-payment")]
    public async Task<ActionResult<PublicPortalPayResultDto>> PayDownPayment(string token, CancellationToken ct)
    {
        try
        {
            var result = await verificationService.PayDownPaymentAsync(
                token, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
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

    /// <summary>The installment-payment link's public page: the customer's open installments
    /// across all their policies, ordered by due date. The "pay" literal cannot collide with a
    /// token — real tokens are 43 chars of Base64Url.</summary>
    [HttpGet("pay/{token}")]
    public async Task<ActionResult<PublicPaymentLinkInfoDto>> GetPaymentLinkInfo(string token, CancellationToken ct)
    {
        try
        {
            var info = await paymentLinkService.GetOpenInstallmentsAsync(token, ct);
            return Ok(new PublicPaymentLinkInfoDto(
                info.CustomerDisplayName,
                info.AgencyName,
                info.Installments.Select(i => new PublicPaymentLinkInstallmentDto(
                    i.Id, i.SeqNo, i.PolicyNumber, i.DueDate, i.BalanceToman, i.IsOverdue)).ToList()));
        }
        catch (PortalInvitationException ex)
        {
            return NotFoundProblem(ex.Message);
        }
    }

    /// <summary>Pays one installment at its full remaining balance through the agency's own
    /// gateway — the online counterpart of an agent-recorded receipt.</summary>
    [HttpPost("pay/{token}/installments/{installmentId:guid}")]
    public async Task<ActionResult<PublicPaymentLinkPayResultDto>> PayInstallment(
        string token, Guid installmentId, CancellationToken ct)
    {
        try
        {
            var result = await paymentLinkService.PayInstallmentAsync(
                token, installmentId, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            return Ok(new PublicPaymentLinkPayResultDto(
                result.PaidAmountToman, result.PaidAtUtc, result.PolicyNumber, result.SeqNo));
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
