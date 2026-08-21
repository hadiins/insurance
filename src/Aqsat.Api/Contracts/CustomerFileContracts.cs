namespace Aqsat.Api.Contracts;

public sealed record CustomerListItemDto(Guid Id, string FullName, string? Mobile, int PolicyCount);

public sealed record CustomerFileDto(
    Guid CustomerId,
    string FullName,
    string? Mobile,
    string? NationalIdMasked,
    decimal AggregateBalance,
    IReadOnlyList<CustomerPolicySummaryDto> Policies,
    IReadOnlyList<CustomerPaymentDto> Payments,
    IReadOnlyList<CustomerCollateralDto> Collateral,
    IReadOnlyList<TimelineEntryDto> Timeline);

public sealed record CustomerPolicySummaryDto(
    Guid PolicyId, string PolicyNumber, string InsuranceLineNameFa, string Status, decimal TotalReceivable, decimal Balance);

public sealed record CustomerPaymentDto(
    Guid Id, decimal Amount, DateOnly PaidOn, string Method, string? ReferenceNo, IReadOnlyList<string> AllocatedTo);

public sealed record CustomerCollateralDto(
    Guid Id, string PolicyNumber, string Type, decimal Amount, DateOnly? DueDate, string Status);
