namespace Aqsat.Api.Contracts;

public sealed record PendingSchedulePolicyDto(
    Guid PolicyId, string PolicyNumber, string CustomerFullName, string ContractName, decimal TotalPremium);

public sealed record ScheduleRequest(decimal DownPayment, int InstallmentCount);

public sealed record InstallmentDto(int SeqNo, DateOnly DueDate, decimal Amount);

public sealed record ScheduleResultDto(Guid PolicyId, bool ExceedsMaxInstallments, IReadOnlyList<InstallmentDto> Installments);

public sealed record BatchScheduleItem(Guid PolicyId, decimal DownPayment, int InstallmentCount);

public sealed record BatchScheduleResultDto(Guid PolicyId, ScheduleResultDto? Result, string? Error);
