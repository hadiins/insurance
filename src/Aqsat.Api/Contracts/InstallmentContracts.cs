namespace Aqsat.Api.Contracts;

/// <summary>At least one of Amount/DueDate must be set (niaz #3). ShiftFollowing re-lays every
/// later unsettled installment of the same policy on monthly cadence anchored to the new due date
/// (Jalali same-day-of-month, the issuance rule) instead of leaving them on the old dates.</summary>
public sealed record UpdateInstallmentRequest(decimal? Amount, DateOnly? DueDate, bool ShiftFollowing = false);

public sealed record InstallmentDetailDto(
    Guid Id, int SeqNo, DateOnly DueDate, DateOnly SettlementDeadline, decimal Amount, decimal PaidAmount,
    string Status, bool IsManuallyEdited);

/// <summary>Backs اقساط معوق / تسویه‌های جزئی — the countdown dashboard's window is deliberately
/// narrow (30 days back, 7 ahead); this worklist has no window at all, since "every overdue
/// installment" and "every partially-settled installment" are open-ended by nature.</summary>
public sealed record InstallmentWorklistRowDto(
    Guid InstallmentId, Guid PolicyId, string PolicyNumber, string CustomerFullName, string? CustomerMobile,
    int SeqNo, DateOnly DueDate, DateOnly SettlementDeadline, decimal Amount, decimal PaidAmount, decimal Balance,
    string Status, string Urgency);
