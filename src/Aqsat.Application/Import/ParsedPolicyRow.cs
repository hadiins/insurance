namespace Aqsat.Application.Import;

/// <summary>
/// A fully validated, type-coerced row ready to persist as Customer/Vehicle/Policy — the shared
/// shape both the generic column-mapped importer (Task 6) and the Fanavaran adapter (Task 7)
/// produce, so the actual entity-creation logic in ImportService lives in exactly one place.
/// </summary>
public sealed record ParsedPolicyRow(
    string PolicyNumber,
    string CustomerExternalCode,
    string CustomerFullName,
    string? CustomerMobile,
    string? CustomerNationalId,
    string? VehiclePlate,
    string? VehicleVin,
    string? VehicleChassis,
    string? VehicleMake,
    string? VehicleModel,
    int? VehicleYear,
    DateOnly IssueDate,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalPremium,
    string ContractName);
