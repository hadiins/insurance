using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>Stage 7/7 — the agency's own operating costs (rent, salaries, …), a real third expense
/// line in the P&amp;L alongside marketer commission and default write-off. AuditEntry is written
/// automatically by AppDbContext.SaveChangesAsync (Expense implements IAuditableEntity).</summary>
[ApiController]
[Route("api/expenses")]
[Authorize(Policy = Permissions.FinanceRead)]
public sealed class ExpensesController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ExpensesReportDto>> Get([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        if (to < from)
        {
            return ValidationProblem("بازهٔ تاریخ نامعتبر است.");
        }

        var expenses = await dbContext.Expenses.AsNoTracking()
            .Where(e => e.Date >= from && e.Date <= to)
            .Include(e => e.Category)
            .Include(e => e.CashBox)
            .Include(e => e.BankAccount)
            .OrderByDescending(e => e.Date)
            .ToListAsync(ct);

        var rows = expenses
            .Select(e => new ExpenseDto(
                e.Id, e.Title, e.Amount, e.Date, e.CategoryId, e.Category.Name, e.MethodType.ToString(),
                e.CashBox?.Name, e.BankAccount is null ? null : $"{e.BankAccount.BankName} — {e.BankAccount.AccountNumber}"))
            .ToList();

        var byCategory = rows
            .GroupBy(r => r.CategoryName)
            .Select(g => new ExpenseByCategoryRow(g.Key, g.Count(), g.Sum(r => r.Amount)))
            .OrderByDescending(r => r.Amount)
            .ToList();

        return Ok(new ExpensesReportDto(rows.Sum(r => r.Amount), rows.Count, byCategory, rows));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.PaymentWrite)]
    public async Task<ActionResult<ExpenseDto>> Create(CreateExpenseRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return ValidationProblem("عنوان هزینه الزامی است.");
        }

        if (request.Amount <= 0)
        {
            return ValidationProblem("مبلغ هزینه باید مثبت باشد.");
        }

        var category = await dbContext.ExpenseCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct);
        if (category is null)
        {
            return ValidationProblem("دستهٔ هزینه یافت نشد.");
        }

        var entity = new Expense
        {
            AgencyId = currentUser.ActiveOrganizationId,
            Title = request.Title.Trim(),
            Amount = request.Amount,
            Date = request.Date,
            CategoryId = request.CategoryId,
            MethodType = request.MethodType,
            CashBoxId = request.CashBoxId,
            BankAccountId = request.BankAccountId,
        };
        dbContext.Expenses.Add(entity);
        await dbContext.SaveChangesAsync(ct);

        var saved = await dbContext.Expenses.AsNoTracking()
            .Include(e => e.CashBox)
            .Include(e => e.BankAccount)
            .FirstAsync(e => e.Id == entity.Id, ct);

        return Ok(new ExpenseDto(
            saved.Id, saved.Title, saved.Amount, saved.Date, saved.CategoryId, category.Name, saved.MethodType.ToString(),
            saved.CashBox?.Name, saved.BankAccount is null ? null : $"{saved.BankAccount.BankName} — {saved.BankAccount.AccountNumber}"));
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
