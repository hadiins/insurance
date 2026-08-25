using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Payments;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// Stage 5/7 — چک بابت پرداخت قسط، distinct from Collateral (which is guarantee-only cheques never
/// meant to be cashed). Status lifecycle mirrors Collateral's (Held → AtBank → Cleared/Bounced); a
/// transition to Bounced drives the same unwind PaymentsController.Reverse performs, via the shared
/// PaymentReversalService, so a bounced cheque is never a second, divergent reversal path.
/// </summary>
[ApiController]
[Route("api/payment-cheques")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class PaymentChequesController(
    AppDbContext dbContext, ICurrentUserContext currentUser, PaymentReversalService reversalService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PaymentChequeDto>>> List([FromQuery] string? status, CancellationToken ct)
    {
        var query = dbContext.PaymentCheques.AsNoTracking()
            .Include(c => c.Payment)
            .Include(c => c.Policy)
            .Include(c => c.CashBox)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<CollateralStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(c => c.Status == parsedStatus);
        }

        var items = await query
            .OrderBy(c => c.DueDate)
            .Select(c => new PaymentChequeDto(
                c.Id, c.PolicyId, c.Policy.PolicyNumber, c.Payment.Customer.FullName,
                c.ChequeNumber, c.BankName, c.DueDate, c.PresenterName,
                c.CashBox.Name, c.Payment.Amount, c.Status.ToString()))
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = Permissions.PaymentWrite)]
    public async Task<ActionResult<PaymentChequeDto>> UpdateStatus(Guid id, UpdatePaymentChequeStatusRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<CollateralStatus>(request.Status, ignoreCase: true, out var status))
        {
            return ValidationProblem("وضعیت نامعتبر است.");
        }

        var cheque = await dbContext.PaymentCheques
            .Include(c => c.Payment).ThenInclude(p => p.Customer)
            .Include(c => c.Policy)
            .Include(c => c.CashBox)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cheque is null)
        {
            return NotFound();
        }

        var wasBounced = cheque.Status == CollateralStatus.Bounced;
        cheque.Status = status;

        // Bouncing must undo whatever the cheque's receipt made payable — same path a manual
        // reversal takes, never a second copy of the unwind logic.
        if (status == CollateralStatus.Bounced && !wasBounced)
        {
            await reversalService.ReverseAsync(cheque.PaymentId, currentUser.UserId, currentUser.DisplayName, ct);
        }

        await dbContext.SaveChangesAsync(ct);

        return Ok(new PaymentChequeDto(
            cheque.Id, cheque.PolicyId, cheque.Policy.PolicyNumber, cheque.Payment.Customer.FullName,
            cheque.ChequeNumber, cheque.BankName, cheque.DueDate, cheque.PresenterName,
            cheque.CashBox.Name, cheque.Payment.Amount, cheque.Status.ToString()));
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
