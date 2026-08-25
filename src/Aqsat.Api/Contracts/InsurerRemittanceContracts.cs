using Aqsat.Domain.Enums;

namespace Aqsat.Api.Contracts;

/// <summary>An installment (or down-payment/full-payment receipt, InstallmentId null) already
/// collected from the customer but not yet remitted to the insurer.</summary>
public sealed record PendingRemittanceRow(
    Guid PolicyId, string PolicyNumber, string CustomerFullName, string InsuranceLineNameFa,
    Guid? InstallmentId, int? SeqNo, decimal Amount, DateOnly CollectedOn);

public sealed record RemittanceLineRequest(Guid PolicyId, Guid? InstallmentId, decimal Amount);

public sealed record CreateInsurerRemittanceRequest(
    DateOnly Date, PaymentMethod MethodType, Guid? CashBoxId, Guid? BankAccountId, string? ReferenceNo,
    IReadOnlyList<RemittanceLineRequest> Lines);

public sealed record InsurerRemittanceLineDto(Guid PolicyId, string PolicyNumber, int? SeqNo, decimal Amount);

public sealed record InsurerRemittanceDto(
    Guid Id, DateOnly Date, decimal Amount, string MethodType, string? CashBoxName, string? BankAccountLabel,
    string? ReferenceNo, IReadOnlyList<InsurerRemittanceLineDto> Lines);
