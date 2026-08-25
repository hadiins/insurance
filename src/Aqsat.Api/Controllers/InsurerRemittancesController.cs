using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// «پرداخت به بیمه‌گر» — the agency's own outgoing remittance of already-collected installment/
/// premium money to the insurer, within the settlement deadline (CLAUDE.md's "3-day" rule). Each
/// batch links to specific settled installments (or down-payment/full-payment receipts), never a
/// bare lump sum, so a per-policy/per-installment remittance history stays queryable.
/// </summary>
[ApiController]
[Route("api/insurer-remittances")]
[Authorize(Policy = Permissions.FinanceRead)]
public sealed class InsurerRemittancesController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet("pending")]
    public async Task<ActionResult<IReadOnlyList<PendingRemittanceRow>>> Pending(CancellationToken ct)
    {
        var settledInstallments = await dbContext.Installments
            .AsNoTracking()
            .Where(i => i.Status == Domain.Enums.InstallmentStatus.Settled)
            .Where(i => !dbContext.InsurerRemittanceLines.Any(l => l.InstallmentId == i.Id))
            .Include(i => i.Policy).ThenInclude(p => p.InsuranceLine)
            .Include(i => i.Policy).ThenInclude(p => p.Customer)
            .ToListAsync(ct);

        var lastPaymentByInstallment = await dbContext.PaymentAllocations
            .AsNoTracking()
            .Where(a => settledInstallments.Select(i => i.Id).Contains(a.InstallmentId))
            .GroupBy(a => a.InstallmentId)
            .Select(g => new { InstallmentId = g.Key, LastPaidOn = g.Max(a => a.Payment.PaidOn) })
            .ToDictionaryAsync(g => g.InstallmentId, g => g.LastPaidOn, ct);

        var rows = settledInstallments.Select(i => new PendingRemittanceRow(
            i.PolicyId, i.Policy.PolicyNumber, i.Policy.Customer.FullName, i.Policy.InsuranceLine.NameFa,
            i.Id, i.SeqNo, i.Amount, lastPaymentByInstallment.GetValueOrDefault(i.Id))).ToList();

        // Down-payment and non-installment full-payment receipts have no PaymentAllocation rows —
        // InstallmentIdHint holds the policy's own Id in both cases (no real FK, PaymentConfiguration.cs).
        var barePayments = await dbContext.Payments
            .AsNoTracking()
            .Where(p => !p.Allocations.Any())
            .Where(p => !dbContext.InsurerRemittanceLines.Any(l => l.InstallmentId == null && l.PolicyId == p.InstallmentIdHint))
            .ToListAsync(ct);

        if (barePayments.Count > 0)
        {
            var policyIds = barePayments.Select(p => p.InstallmentIdHint).Distinct().ToList();
            var policiesById = await dbContext.Policies
                .AsNoTracking()
                .Where(p => policyIds.Contains(p.Id))
                .Include(p => p.InsuranceLine)
                .Include(p => p.Customer)
                .ToDictionaryAsync(p => p.Id, ct);

            rows.AddRange(barePayments
                .Where(p => policiesById.ContainsKey(p.InstallmentIdHint))
                .Select(p =>
                {
                    var policy = policiesById[p.InstallmentIdHint];
                    return new PendingRemittanceRow(
                        policy.Id, policy.PolicyNumber, policy.Customer.FullName, policy.InsuranceLine.NameFa,
                        null, null, p.Amount, p.PaidOn);
                }));
        }

        return Ok(rows.OrderBy(r => r.CollectedOn).ToList());
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InsurerRemittanceDto>>> List(CancellationToken ct)
    {
        var remittances = await dbContext.InsurerRemittances
            .AsNoTracking()
            .Include(r => r.CashBox)
            .Include(r => r.BankAccount)
            .Include(r => r.Lines).ThenInclude(l => l.Policy)
            .Include(r => r.Lines).ThenInclude(l => l.Installment)
            .OrderByDescending(r => r.Date)
            .ToListAsync(ct);

        var result = remittances.Select(r => new InsurerRemittanceDto(
            r.Id, r.Date, r.Amount, r.MethodType.ToString(), r.CashBox?.Name,
            r.BankAccount is null ? null : $"{r.BankAccount.BankName} — {r.BankAccount.AccountNumber}",
            r.ReferenceNo,
            r.Lines.Select(l => new InsurerRemittanceLineDto(l.PolicyId, l.Policy.PolicyNumber, l.Installment?.SeqNo, l.Amount)).ToList()));

        return Ok(result.ToList());
    }

    [HttpPost]
    [Authorize(Policy = Permissions.PaymentWrite)]
    public async Task<ActionResult<InsurerRemittanceDto>> Create(CreateInsurerRemittanceRequest request, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
        {
            return ValidationProblem("حداقل یک ردیف باید انتخاب شود.");
        }

        var installmentIds = request.Lines.Where(l => l.InstallmentId is not null).Select(l => l.InstallmentId!.Value).ToList();
        var alreadyRemitted = await dbContext.InsurerRemittanceLines.AsNoTracking()
            .Where(l => l.InstallmentId != null && installmentIds.Contains(l.InstallmentId!.Value))
            .Select(l => l.InstallmentId!.Value)
            .ToListAsync(ct);
        if (alreadyRemitted.Count > 0)
        {
            return ValidationProblem("یک یا چند قسط انتخاب‌شده قبلاً به بیمه‌گر واریز شده است.");
        }

        var remittance = new InsurerRemittance
        {
            AgencyId = currentUser.ActiveOrganizationId,
            Date = request.Date,
            Amount = request.Lines.Sum(l => l.Amount),
            MethodType = request.MethodType,
            CashBoxId = request.CashBoxId,
            BankAccountId = request.BankAccountId,
            ReferenceNo = request.ReferenceNo,
        };

        foreach (var line in request.Lines)
        {
            remittance.Lines.Add(new InsurerRemittanceLine
            {
                AgencyId = currentUser.ActiveOrganizationId,
                InsurerRemittance = remittance,
                PolicyId = line.PolicyId,
                InstallmentId = line.InstallmentId,
                Amount = line.Amount,
            });
        }

        dbContext.InsurerRemittances.Add(remittance);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return ValidationProblem("یک یا چند قسط انتخاب‌شده هم‌زمان توسط کاربر دیگری واریز شده است.");
        }

        var saved = await dbContext.InsurerRemittances.AsNoTracking()
            .Include(r => r.CashBox)
            .Include(r => r.BankAccount)
            .Include(r => r.Lines).ThenInclude(l => l.Policy)
            .Include(r => r.Lines).ThenInclude(l => l.Installment)
            .FirstAsync(r => r.Id == remittance.Id, ct);

        return Ok(new InsurerRemittanceDto(
            saved.Id, saved.Date, saved.Amount, saved.MethodType.ToString(), saved.CashBox?.Name,
            saved.BankAccount is null ? null : $"{saved.BankAccount.BankName} — {saved.BankAccount.AccountNumber}",
            saved.ReferenceNo,
            saved.Lines.Select(l => new InsurerRemittanceLineDto(l.PolicyId, l.Policy.PolicyNumber, l.Installment?.SeqNo, l.Amount)).ToList()));
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
