namespace Aqsat.Api.Contracts;

public sealed record MarketerDto(Guid Id, string FullName, string Mobile, string Type, bool IsActive, Guid? AppUserId);

public sealed record CreateMarketerRequest(string FullName, string Mobile, string? NationalId, string Type, Guid? AppUserId);

public sealed record UpdateMarketerRequest(string FullName, string Mobile, bool IsActive);

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

public sealed record PayCommissionsRequest(IReadOnlyList<Guid> CommissionEntryIds);

public sealed record PayCommissionsResultDto(Guid PaymentBatchId, int Count, decimal TotalAmount);

/// <summary>Status only, never amounts — docs/PHASE-1-SPEC.md §2.2's marketer visibility limits.</summary>
public sealed record MarketerCustomerDto(Guid CustomerId, string FullName, bool IsOverdue);
