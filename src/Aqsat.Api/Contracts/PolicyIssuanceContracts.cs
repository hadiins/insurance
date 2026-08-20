namespace Aqsat.Api.Contracts;

public sealed record InsuranceLineDto(
    Guid Id, Guid? ParentId, string Code, string NameFa, bool RequiresVehicle, bool RequiresProperty, int SortOrder);

public sealed record VehicleInput(string? Plate, string? Vin, string? Chassis, string? Make, string? Model, int? Year);

public sealed record PropertySubjectInput(string Address, string? PostalCode, string? Type, decimal? Value);

/// <summary>
/// Deliberately excludes DownPayment/InstallmentCount — schedule generation is a separate step
/// (the existing POST /api/policies/{id}/schedule from Task 8), reused as-is rather than
/// duplicated. Field order in the frontend form follows docs/PHASE-1-SPEC.md §5, verbatim: line ->
/// customer -> insured subject -> premium -> service fee -> (down payment/schedule happens next).
/// </summary>
public sealed record CreatePolicyRequest(
    string PolicyNumber,
    Guid InsuranceLineId,
    Guid? CustomerId,
    string? CustomerFullName,
    string? CustomerMobile,
    string? CustomerNationalId,
    VehicleInput? Vehicle,
    PropertySubjectInput? Property,
    DateOnly IssueDate,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal NetPremium,
    decimal ServiceFee,
    Guid? MarketerId,
    string? PreviousInsurer,
    bool IsRenewal);

public sealed record CreatePolicyResultDto(Guid PolicyId, string PolicyNumber, Guid CustomerId);
