using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// Stage 6/7 — «گزارش دریافتی‌ها»: every real receipt (down payment, installment payment,
/// non-installment full payment) in a date range, split by MethodType (صندوق/بانک/چک) with a
/// cheque-status breakdown. Reads Payment directly, same "no heavy join across operational tables"
/// discipline CollectionsReportController documents.
/// </summary>
[ApiController]
[Route("api/reports/receipts")]
[Authorize(Policy = Permissions.ReportRead)]
public sealed class ReceiptsReportController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ReceiptsReportDto>> Get([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        if (to < from)
        {
            return ValidationProblem("بازهٔ تاریخ نامعتبر است.");
        }

        var payments = await dbContext.Payments
            .AsNoTracking()
            .Where(p => p.PaidOn >= from && p.PaidOn <= to)
            .Include(p => p.Customer)
            .Include(p => p.CashBox)
            .Include(p => p.BankAccount)
            .Include(p => p.Allocations).ThenInclude(a => a.Installment).ThenInclude(i => i.Policy)
            .ToListAsync(ct);

        var chequesByPaymentId = await dbContext.PaymentCheques
            .AsNoTracking()
            .Where(c => payments.Select(p => p.Id).Contains(c.PaymentId))
            .ToDictionaryAsync(c => c.PaymentId, ct);

        // A down-payment or non-installment full-payment receipt has no PaymentAllocation rows —
        // InstallmentIdHint holds the policy's own Id in both cases (no real FK, PaymentConfiguration.cs).
        var barePolicyIds = payments.Where(p => p.Allocations.Count == 0).Select(p => p.InstallmentIdHint).Distinct().ToList();
        var barePoliciesById = await dbContext.Policies
            .AsNoTracking()
            .Where(p => barePolicyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var rows = payments
            .Select(p =>
            {
                var policy = p.Allocations.Count > 0
                    ? p.Allocations.First().Installment.Policy
                    : barePoliciesById.GetValueOrDefault(p.InstallmentIdHint);
                var cheque = chequesByPaymentId.GetValueOrDefault(p.Id);

                return new ReceiptRow(
                    p.Id, p.PaidOn, policy?.PolicyNumber ?? "—", p.Customer.FullName, p.Amount,
                    p.MethodType.ToString(), p.ReferenceNo, p.CashBox?.Name,
                    p.BankAccount is null ? null : $"{p.BankAccount.BankName} — {p.BankAccount.AccountNumber}",
                    cheque?.ChequeNumber, cheque?.Status.ToString());
            })
            .OrderByDescending(r => r.PaidOn)
            .ToList();

        var byMethod = rows
            .GroupBy(r => r.MethodType)
            .Select(g => new ReceiptsByMethodRow(g.Key, g.Count(), g.Sum(r => r.Amount)))
            .OrderByDescending(r => r.Amount)
            .ToList();

        var byChequeStatus = rows
            .Where(r => r.ChequeStatus is not null)
            .GroupBy(r => r.ChequeStatus!)
            .Select(g => new ReceiptsByChequeStatusRow(g.Key, g.Count(), g.Sum(r => r.Amount)))
            .OrderByDescending(r => r.Amount)
            .ToList();

        return Ok(new ReceiptsReportDto(rows.Sum(r => r.Amount), rows.Count, byMethod, byChequeStatus, rows));
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
