using Aqsat.Domain.Enums;

namespace Aqsat.Api.Contracts;

public sealed record ExpenseCategoryDto(Guid Id, string Name, bool IsActive);

public sealed record CreateExpenseCategoryRequest(string Name);

public sealed record UpdateExpenseCategoryRequest(string Name, bool IsActive);

public sealed record CreateExpenseRequest(
    string Title, decimal Amount, DateOnly Date, Guid CategoryId,
    PaymentMethod MethodType, Guid? CashBoxId, Guid? BankAccountId);

public sealed record ExpenseDto(
    Guid Id, string Title, decimal Amount, DateOnly Date,
    Guid CategoryId, string CategoryName, string MethodType, string? CashBoxName, string? BankAccountLabel);

public sealed record ExpenseByCategoryRow(string CategoryName, int Count, decimal Amount);

public sealed record ExpensesReportDto(decimal TotalAmount, int Count, IReadOnlyList<ExpenseByCategoryRow> ByCategory, IReadOnlyList<ExpenseDto> Rows);
