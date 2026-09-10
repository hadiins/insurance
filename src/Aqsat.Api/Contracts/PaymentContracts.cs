using System.ComponentModel.DataAnnotations;
using Aqsat.Domain.Enums;

namespace Aqsat.Api.Contracts;

/// <summary>An explicit override for one installment's share of the payment — omit
/// <see cref="RecordPaymentRequest.Allocations"/> entirely to let the server allocate automatically
/// per docs/PHASE-1-SPEC.md §3.4 (oldest due date first); pass it to let the agent edit the split.</summary>
public sealed record AllocationLineRequest(Guid InstallmentId, decimal Amount);

/// <summary>Required when MethodType == Cheque, on every receipt endpoint that accepts a
/// structured method (installment payment, down payment, non-installment full payment). CashBoxId
/// is where the paper cheque is physically held while Status == Held — a different concern from
/// Payment.BankAccountId, which only matters once the cheque is actually deposited.</summary>
public sealed record ChequeDetailsRequest(
    string ChequeNumber, string BankName, DateOnly DueDate, string PresenterName, Guid CashBoxId);

/// <summary>Method/ReferenceNo caps mirror the Payment columns (B14): nvarchar(30)/nvarchar(60).</summary>
public sealed record RecordPaymentRequest(
    Guid InstallmentIdHint,
    decimal Amount,
    DateOnly PaidOn,
    [MaxLength(30)] string Method,
    [MaxLength(60)] string? ReferenceNo,
    IReadOnlyList<AllocationLineRequest>? Allocations = null,
    PaymentMethod? MethodType = null,
    Guid? CashBoxId = null,
    Guid? BankAccountId = null,
    ChequeDetailsRequest? Cheque = null);

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
    [MaxLength(30)] string Method,
    [MaxLength(60)] string? ReferenceNo,
    PaymentMethod? MethodType = null,
    Guid? CashBoxId = null,
    Guid? BankAccountId = null,
    ChequeDetailsRequest? Cheque = null);

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
    Guid? BankAccountId = null,
    ChequeDetailsRequest? Cheque = null);

public sealed record ReceiveDownPaymentResultDto(Guid PaymentId, decimal Amount);
