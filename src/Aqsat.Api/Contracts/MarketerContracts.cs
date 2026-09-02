using Aqsat.Domain.Enums;

namespace Aqsat.Api.Contracts;

/// <summary>AppUserFullName/AppUserMobile describe the linked panel login (null when unlinked) —
/// the login's mobile may differ from the marketer's own contact number.</summary>
public sealed record MarketerDto(
    Guid Id, string FullName, string Mobile, string Type, bool IsActive, Guid? AppUserId,
    string? AppUserFullName = null, string? AppUserMobile = null);

public sealed record CreateMarketerRequest(string FullName, string Mobile, string? NationalId, string Type, Guid? AppUserId);

public sealed record UpdateMarketerRequest(string FullName, string Mobile, bool IsActive);

/// <summary>Creates (or reuses) the AppUser login behind a marketer's panel access and links it via
/// Marketer.AppUserId. Mobile defaults to the marketer's own in the UI; FullName only matters when
/// the AppUser is being created for the first time.</summary>
public sealed record CreateMarketerPanelAccessRequest(string Mobile, string Password, string? FullName);

public sealed record MarketerRateDto(
    Guid Id, Guid InsuranceLineId, string InsuranceLineName, decimal RatePercent, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

/// <summary>Adding a rate never updates a prior row in place — it closes the previous one's
/// EffectiveTo and inserts a new row, so CommissionEntry.RatePercent values already locked at
/// issuance stay meaningful (docs/PHASE-1-SPEC.md §2.2).</summary>
public sealed record SetMarketerRateRequest(Guid InsuranceLineId, decimal RatePercent, DateOnly EffectiveFrom);

public sealed record CommissionEntryDto(
    Guid Id, Guid PolicyId, string PolicyNumber, int? InstallmentSeqNo, decimal Amount, string Status,
    DateTimeOffset? EligibleAt, DateTimeOffset? PaidAt);

public sealed record CommissionSummaryDto(decimal Pending, decimal Payable, decimal Paid, IReadOnlyList<CommissionEntryDto> Entries);

/// <summary>The payment details are optional for backward compatibility, but when given they also
/// write a CommissionPayout money-outflow row — without it, paying a marketer never moved cash out
/// of any CashBox/BankAccount. MethodType/CashBoxId/BankAccountId follow the same rules as every
/// other money form (Cash → box, BankTransfer → account).</summary>
public sealed record PayCommissionsRequest(
    IReadOnlyList<Guid> CommissionEntryIds,
    PaymentMethod MethodType = PaymentMethod.Cash,
    Guid? CashBoxId = null,
    Guid? BankAccountId = null,
    DateOnly? PaidOn = null,
    string? ReferenceNo = null);

public sealed record PayCommissionsResultDto(Guid PaymentBatchId, int Count, decimal TotalAmount);

/// <summary>Status only, never amounts — docs/PHASE-1-SPEC.md §2.2's marketer visibility limits.</summary>
public sealed record MarketerCustomerDto(Guid CustomerId, string FullName, bool IsOverdue);
