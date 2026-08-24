using Aqsat.Domain.Enums;

namespace Aqsat.Api.Contracts;

/// <summary>An explicit override for one installment's share of the payment — omit
/// <see cref="RecordPaymentRequest.Allocations"/> entirely to let the server allocate automatically
/// per docs/PHASE-1-SPEC.md §3.4 (oldest due date first); pass it to let the agent edit the split.</summary>
public sealed record AllocationLineRequest(Guid InstallmentId, decimal Amount);

public sealed record RecordPaymentRequest(
    Guid InstallmentIdHint,
    decimal Amount,
    DateOnly PaidOn,
    string Method,
    string? ReferenceNo,
    IReadOnlyList<AllocationLineRequest>? Allocations = null,
    PaymentMethod? MethodType = null,
    Guid? CashBoxId = null,
    Guid? BankAccountId = null);

public sealed record AllocationLineDto(Guid InstallmentId, int SeqNo, string PolicyNumber, decimal Amount);

public sealed record PaymentResultDto(
    Guid PaymentId,
    decimal Amount,
    IReadOnlyList<AllocationLineDto> Allocations,
    decimal UnallocatedAmount);

public sealed record BatchPaymentResultItem(Guid InstallmentIdHint, PaymentResultDto? Result, string? Error);
