namespace Aqsat.Api.Contracts;

/// <summary>Backs the three "list of policies" menus (فهرست بیمه‌نامه‌ها / بیمه‌نامه‌های اقساطی /
/// باطل‌شده‌ها) — one endpoint, filtered client-side by which nav item opened the tab.</summary>
public sealed record PolicyListItemDto(
    Guid Id, string PolicyNumber, string CustomerFullName, string InsuranceLineNameFa,
    string Status, bool IsInstallment, decimal TotalReceivable, decimal Balance, DateOnly IssueDate);
