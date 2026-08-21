namespace Aqsat.Api.Contracts;

public sealed record PolicyFileDto(
    Guid PolicyId,
    string PolicyNumber,
    Guid CustomerId,
    string CustomerFullName,
    string InsuranceLineNameFa,
    string Status,
    decimal NetPremium,
    decimal ServiceFee,
    decimal TotalReceivable,
    decimal DownPayment,
    string? MarketerFullName,
    IReadOnlyList<PolicyInstallmentDto> Installments,
    IReadOnlyList<PolicyEndorsementDto> Endorsements,
    IReadOnlyList<PolicyCommissionRowDto> Commissions,
    IReadOnlyList<TimelineEntryDto> Timeline);

public sealed record PolicyInstallmentDto(
    Guid Id, int SeqNo, DateOnly DueDate, DateOnly SettlementDeadline, decimal Amount, decimal PaidAmount, decimal Balance, string Status);

public sealed record PolicyEndorsementDto(
    Guid Id, string EndorsementNo, string Type, DateOnly IssueDate, decimal PremiumDelta, decimal ServiceFeeDelta, string? Description);

public sealed record PolicyCommissionRowDto(
    Guid Id, int? InstallmentSeqNo, decimal Amount, string Status, DateTimeOffset? EligibleAt, DateTimeOffset? PaidAt);
