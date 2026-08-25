namespace Aqsat.Api.Contracts;

/// <summary>docs/TASKS.md Task 15 — accrual/cash toggle + configurable write-off threshold.</summary>
public sealed record PnlRequest(DateOnly From, DateOnly To, string Basis, int? WriteOffThresholdDays);

public sealed record PnlBreakdownRow(
    string GroupKey, string GroupLabel,
    decimal AgencyCommissionIncome, decimal ServiceFeeIncome, decimal MarketerCommissionExpense, decimal DefaultWriteOffExpense,
    decimal TotalIncome, decimal TotalExpense, decimal NetProfit, decimal OperatingExpense = 0);

public sealed record PnlResultDto(
    decimal AgencyCommissionIncome, decimal ServiceFeeIncome, decimal MarketerCommissionExpense, decimal DefaultWriteOffExpense,
    decimal TotalIncome, decimal TotalExpense, decimal NetProfit,
    IReadOnlyList<PnlBreakdownRow> ByLine, IReadOnlyList<PnlBreakdownRow> ByMarketer, IReadOnlyList<PnlBreakdownRow> ByMonth,
    decimal OperatingExpense = 0);

/// <summary>docs/TASKS.md Task 18 — filtered, server-side-paged collections report. Reads only
/// indexed columns on Installment (AgencyId, Status, SettlementDeadline) — no heavy join on live
/// operational tables (CLAUDE.md's explicit warning).</summary>
public sealed record CollectionsReportRow(
    string PolicyNumber, string CustomerFullName, string InsuranceLineNameFa, int SeqNo,
    DateOnly DueDate, DateOnly SettlementDeadline, decimal Amount, decimal PaidAmount, decimal Balance, string Status);

public sealed record CollectionsReportPageDto(int TotalCount, IReadOnlyList<CollectionsReportRow> Rows);

public sealed record CollectionsSummaryDto(
    int TotalCount, decimal TotalDue, decimal TotalCollected,
    int SettledOnTimeCount, int SettledLateCount, int OpenCount, int WrittenOffCount,
    decimal OnTimeRate, decimal DefaultRate);
