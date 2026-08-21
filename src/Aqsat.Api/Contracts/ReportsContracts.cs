namespace Aqsat.Api.Contracts;

/// <summary>docs/TASKS.md Task 15 — accrual/cash toggle + configurable write-off threshold.</summary>
public sealed record PnlRequest(DateOnly From, DateOnly To, string Basis, int? WriteOffThresholdDays);

public sealed record PnlBreakdownRow(
    string GroupKey, string GroupLabel,
    decimal AgencyCommissionIncome, decimal ServiceFeeIncome, decimal MarketerCommissionExpense, decimal DefaultWriteOffExpense,
    decimal TotalIncome, decimal TotalExpense, decimal NetProfit);

public sealed record PnlResultDto(
    decimal AgencyCommissionIncome, decimal ServiceFeeIncome, decimal MarketerCommissionExpense, decimal DefaultWriteOffExpense,
    decimal TotalIncome, decimal TotalExpense, decimal NetProfit,
    IReadOnlyList<PnlBreakdownRow> ByLine, IReadOnlyList<PnlBreakdownRow> ByMarketer, IReadOnlyList<PnlBreakdownRow> ByMonth);
