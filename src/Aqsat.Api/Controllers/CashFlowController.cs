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
/// «موجودی و گردش صندوق و بانک» — the live balance of every CashBox/BankAccount and the unified
/// movement view behind it. "P&amp;L is not accounting" (CLAUDE.md): no ledger, no double entry —
/// opening balance plus arithmetic over the flows the system already records (Payments in;
/// Expenses, InsurerRemittances and CommissionPayouts out; FundTransfers both ways).
/// </summary>
[ApiController]
[Route("api/cash-flow")]
[Authorize(Policy = Permissions.FinanceRead)]
public sealed class CashFlowController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet("balances")]
    public async Task<ActionResult<CashFlowBalancesDto>> Balances(CancellationToken ct)
    {
        var boxes = await dbContext.CashBoxes.AsNoTracking().Where(b => !b.IsDeleted).OrderBy(b => b.Name).ToListAsync(ct);
        var accounts = await dbContext.BankAccounts.AsNoTracking().Where(a => !a.IsDeleted).OrderBy(a => a.BankName).ToListAsync(ct);

        var boxIds = boxes.Select(b => b.Id).ToList();
        var accountIds = accounts.Select(a => a.Id).ToList();

        var payments = await dbContext.Payments.AsNoTracking()
            .Where(p => !p.IsDeleted
                && ((p.CashBoxId != null && boxIds.Contains(p.CashBoxId.Value)) || (p.BankAccountId != null && accountIds.Contains(p.BankAccountId.Value))))
            .Select(p => new { p.CashBoxId, p.BankAccountId, p.Amount })
            .ToListAsync(ct);
        var expenses = await dbContext.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted
                && ((e.CashBoxId != null && boxIds.Contains(e.CashBoxId.Value)) || (e.BankAccountId != null && accountIds.Contains(e.BankAccountId.Value))))
            .Select(e => new { e.CashBoxId, e.BankAccountId, e.Amount })
            .ToListAsync(ct);
        var remittances = await dbContext.InsurerRemittances.AsNoTracking()
            .Where(r => !r.IsDeleted
                && ((r.CashBoxId != null && boxIds.Contains(r.CashBoxId.Value)) || (r.BankAccountId != null && accountIds.Contains(r.BankAccountId.Value))))
            .Select(r => new { r.CashBoxId, r.BankAccountId, r.Amount })
            .ToListAsync(ct);
        var payouts = await dbContext.CommissionPayouts.AsNoTracking()
            .Where(p => !p.IsDeleted
                && ((p.CashBoxId != null && boxIds.Contains(p.CashBoxId.Value)) || (p.BankAccountId != null && accountIds.Contains(p.BankAccountId.Value))))
            .Select(p => new { p.CashBoxId, p.BankAccountId, p.Amount })
            .ToListAsync(ct);
        var transfers = await dbContext.FundTransfers.AsNoTracking()
            .Where(t => !t.IsDeleted
                && ((t.FromCashBoxId != null && boxIds.Contains(t.FromCashBoxId.Value)) || (t.FromBankAccountId != null && accountIds.Contains(t.FromBankAccountId.Value))
                    || (t.ToCashBoxId != null && boxIds.Contains(t.ToCashBoxId.Value)) || (t.ToBankAccountId != null && accountIds.Contains(t.ToBankAccountId.Value))))
            .Select(t => new { t.FromCashBoxId, t.FromBankAccountId, t.ToCashBoxId, t.ToBankAccountId, t.Amount })
            .ToListAsync(ct);

        var boxRows = boxes.Select(b => new FundBalanceDto(
            b.Id, b.Name, IsBankAccount: false, b.IsActive, b.OpeningBalance,
            TotalIn: payments.Where(p => p.CashBoxId == b.Id).Sum(p => p.Amount)
                + transfers.Where(t => t.ToCashBoxId == b.Id).Sum(t => t.Amount),
            TotalOut: expenses.Where(e => e.CashBoxId == b.Id).Sum(e => e.Amount)
                + remittances.Where(r => r.CashBoxId == b.Id).Sum(r => r.Amount)
                + payouts.Where(p => p.CashBoxId == b.Id).Sum(p => p.Amount)
                + transfers.Where(t => t.FromCashBoxId == b.Id).Sum(t => t.Amount),
            Balance: 0m)).ToList();

        var accountRows = accounts.Select(a => new FundBalanceDto(
            a.Id, $"{a.BankName} — {a.AccountNumber}", IsBankAccount: true, a.IsActive, a.OpeningBalance,
            TotalIn: payments.Where(p => p.BankAccountId == a.Id).Sum(p => p.Amount)
                + transfers.Where(t => t.ToBankAccountId == a.Id).Sum(t => t.Amount),
            TotalOut: expenses.Where(e => e.BankAccountId == a.Id).Sum(e => e.Amount)
                + remittances.Where(r => r.BankAccountId == a.Id).Sum(r => r.Amount)
                + payouts.Where(p => p.BankAccountId == a.Id).Sum(p => p.Amount)
                + transfers.Where(t => t.FromBankAccountId == a.Id).Sum(t => t.Amount),
            Balance: 0m)).ToList();

        boxRows = boxRows.Select(r => r with { Balance = r.OpeningBalance + r.TotalIn - r.TotalOut }).ToList();
        accountRows = accountRows.Select(r => r with { Balance = r.OpeningBalance + r.TotalIn - r.TotalOut }).ToList();

        return Ok(new CashFlowBalancesDto(boxRows, accountRows));
    }

    /// <summary>The unified movement (گردش) view for ONE box or account — every flow the balance is
    /// built from, newest first. Exactly one of cashBoxId/bankAccountId is required.</summary>
    [HttpGet("movements")]
    public async Task<ActionResult<FundMovementsDto>> Movements(
        [FromQuery] Guid? cashBoxId, [FromQuery] Guid? bankAccountId,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (cashBoxId.HasValue == bankAccountId.HasValue)
        {
            return ValidationProblem("دقیقاً یکی از صندوق یا حساب بانکی باید انتخاب شود.");
        }

        var rows = new List<FundMovementDto>();

        var payments = await dbContext.Payments.AsNoTracking()
            .Where(p => !p.IsDeleted && p.PaidOn >= (from ?? DateOnly.MinValue) && p.PaidOn <= (to ?? DateOnly.MaxValue))
            .Where(p => cashBoxId != null ? p.CashBoxId == cashBoxId : p.BankAccountId == bankAccountId)
            .Include(p => p.Customer)
            .ToListAsync(ct);
        rows.AddRange(payments.Select(p => new FundMovementDto(p.PaidOn, "دریافتی مشتری", p.Customer.FullName, p.Amount, null, p.ReferenceNo)));

        var expenses = await dbContext.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.Date >= (from ?? DateOnly.MinValue) && e.Date <= (to ?? DateOnly.MaxValue))
            .Where(e => cashBoxId != null ? e.CashBoxId == cashBoxId : e.BankAccountId == bankAccountId)
            .ToListAsync(ct);
        rows.AddRange(expenses.Select(e => new FundMovementDto(e.Date, "هزینه", e.Title, null, e.Amount, null)));

        var remittances = await dbContext.InsurerRemittances.AsNoTracking()
            .Where(r => !r.IsDeleted && r.Date >= (from ?? DateOnly.MinValue) && r.Date <= (to ?? DateOnly.MaxValue))
            .Where(r => cashBoxId != null ? r.CashBoxId == cashBoxId : r.BankAccountId == bankAccountId)
            .ToListAsync(ct);
        rows.AddRange(remittances.Select(r => new FundMovementDto(r.Date, "پرداخت به بیمه‌گر", "واریز به بیمه‌گر", null, r.Amount, r.ReferenceNo)));

        var payouts = await dbContext.CommissionPayouts.AsNoTracking()
            .Where(p => !p.IsDeleted && p.PaidOn >= (from ?? DateOnly.MinValue) && p.PaidOn <= (to ?? DateOnly.MaxValue))
            .Where(p => cashBoxId != null ? p.CashBoxId == cashBoxId : p.BankAccountId == bankAccountId)
            .Include(p => p.Marketer)
            .ToListAsync(ct);
        rows.AddRange(payouts.Select(p => new FundMovementDto(p.PaidOn, "پورسانت بازاریاب", p.Marketer.FullName, null, p.Amount, p.ReferenceNo)));

        var transfers = await dbContext.FundTransfers.AsNoTracking()
            .Where(t => !t.IsDeleted && t.Date >= (from ?? DateOnly.MinValue) && t.Date <= (to ?? DateOnly.MaxValue))
            .Where(t => cashBoxId != null
                ? t.FromCashBoxId == cashBoxId || t.ToCashBoxId == cashBoxId
                : t.FromBankAccountId == bankAccountId || t.ToBankAccountId == bankAccountId)
            .Include(t => t.FromCashBox).Include(t => t.FromBankAccount).Include(t => t.ToCashBox).Include(t => t.ToBankAccount)
            .ToListAsync(ct);
        foreach (var t in transfers)
        {
            var fromLabel = Label(t.FromCashBox, t.FromBankAccount);
            var toLabel = Label(t.ToCashBox, t.ToBankAccount);
            if (cashBoxId != null && t.FromCashBoxId == cashBoxId || bankAccountId != null && t.FromBankAccountId == bankAccountId)
            {
                rows.Add(new FundMovementDto(t.Date, "انتقال وجه", $"انتقال به {toLabel}", null, t.Amount, t.Note));
            }
            if (cashBoxId != null && t.ToCashBoxId == cashBoxId || bankAccountId != null && t.ToBankAccountId == bankAccountId)
            {
                rows.Add(new FundMovementDto(t.Date, "انتقال وجه", $"انتقال از {fromLabel}", t.Amount, null, t.Note));
            }
        }

        var ordered = rows.OrderByDescending(r => r.Date).ToList();
        return Ok(new FundMovementsDto(
            ordered.Sum(r => r.AmountIn ?? 0), ordered.Sum(r => r.AmountOut ?? 0), ordered));
    }

    [HttpPost("transfers")]
    [Authorize(Policy = Permissions.PaymentWrite)]
    public async Task<ActionResult<FundTransferDto>> Transfer(CreateFundTransferRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0)
        {
            return ValidationProblem("مبلغ انتقال باید مثبت باشد.");
        }

        if (request.FromCashBoxId.HasValue == request.FromBankAccountId.HasValue
            || request.ToCashBoxId.HasValue == request.ToBankAccountId.HasValue)
        {
            return ValidationProblem("مبدأ و مقصد انتقال هرکدام باید دقیقاً یک صندوق یا حساب بانکی باشد.");
        }

        var fromBox = request.FromCashBoxId is null ? null
            : await dbContext.CashBoxes.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.FromCashBoxId && !b.IsDeleted, ct);
        var fromAccount = request.FromBankAccountId is null ? null
            : await dbContext.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.FromBankAccountId && !a.IsDeleted, ct);
        var toBox = request.ToCashBoxId is null ? null
            : await dbContext.CashBoxes.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.ToCashBoxId && !b.IsDeleted, ct);
        var toAccount = request.ToBankAccountId is null ? null
            : await dbContext.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.ToBankAccountId && !a.IsDeleted, ct);

        if ((request.FromCashBoxId != null && fromBox is null) || (request.FromBankAccountId != null && fromAccount is null)
            || (request.ToCashBoxId != null && toBox is null) || (request.ToBankAccountId != null && toAccount is null))
        {
            return ValidationProblem("صندوق یا حساب انتخاب‌شده یافت نشد.");
        }

        var fromKey = request.FromCashBoxId is not null ? $"box:{request.FromCashBoxId}" : $"acc:{request.FromBankAccountId}";
        var toKey = request.ToCashBoxId is not null ? $"box:{request.ToCashBoxId}" : $"acc:{request.ToBankAccountId}";
        if (fromKey == toKey)
        {
            return ValidationProblem("مبدأ و مقصد انتقال نباید یکی باشد.");
        }

        var transfer = new FundTransfer
        {
            AgencyId = currentUser.ActiveOrganizationId,
            Date = request.Date,
            Amount = request.Amount,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            FromCashBoxId = request.FromCashBoxId,
            FromBankAccountId = request.FromBankAccountId,
            ToCashBoxId = request.ToCashBoxId,
            ToBankAccountId = request.ToBankAccountId,
        };
        dbContext.FundTransfers.Add(transfer);
        await dbContext.SaveChangesAsync(ct);

        return Ok(new FundTransferDto(
            transfer.Id, transfer.Date, transfer.Amount,
            Label(fromBox, fromAccount), Label(toBox, toAccount), transfer.Note));
    }

    private static string Label(CashBox? box, BankAccount? account) =>
        box?.Name ?? (account is null ? "؟" : $"{account.BankName} — {account.AccountNumber}");

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
