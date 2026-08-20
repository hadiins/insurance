namespace Aqsat.Api.Contracts;

/// <summary>At least one of Amount/DueDate must be set (niaz #3).</summary>
public sealed record UpdateInstallmentRequest(decimal? Amount, DateOnly? DueDate);

public sealed record InstallmentDetailDto(
    Guid Id, int SeqNo, DateOnly DueDate, DateOnly SettlementDeadline, decimal Amount, decimal PaidAmount,
    string Status, bool IsManuallyEdited);
