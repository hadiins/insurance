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

/// <summary>Non-installment policies have no Installment row to hint at — the whole policy is paid
/// off in one shot, which is also the event that flips its single AgencyCommissionEntry payable.</summary>
public sealed record RecordFullPaymentRequest(
    decimal Amount,
    DateOnly PaidOn,
    string Method,
    string? ReferenceNo,
    PaymentMethod? MethodType = null,
    Guid? CashBoxId = null,
    Guid? BankAccountId = null);

public sealed record RecordFullPaymentResultDto(Guid PaymentId, decimal Amount);

/// <summary>Stage 4/7 — receiving the down payment is now its own event, decoupled from
/// scheduling, with a real date/reference/cashbox instead of the schedule call's own timestamp.
/// No free-text Method here on purpose: Payment.Method stays the fixed "پیش‌پرداخت" marker
/// ReportsController's cash-basis P&amp;L keys off of (PoliciesController.DownPaymentMethod);
/// MethodType/CashBoxId/BankAccountId carry the real cash/bank/cheque choice.</summary>
public sealed record ReceiveDownPaymentRequest(
    DateOnly PaidOn,
    string? ReferenceNo,
    PaymentMethod? MethodType = null,
    Guid? CashBoxId = null,
    Guid? BankAccountId = null);

public sealed record ReceiveDownPaymentResultDto(Guid PaymentId, decimal Amount);
