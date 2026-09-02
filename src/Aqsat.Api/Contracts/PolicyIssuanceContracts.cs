namespace Aqsat.Api.Contracts;

public sealed record InsuranceLineDto(
    Guid Id, Guid? ParentId, string Code, string NameFa, bool RequiresVehicle, bool RequiresProperty, int SortOrder);

/// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §5 — Plate is kept for the rare free-text/legacy path,
/// but the issuance form's plate component always sends the four structured parts, from which
/// PlateNormalized is derived server-side (never trusted from the client as a single string).</summary>
public sealed record VehicleInput(
    string? Plate, string? Vin, string? Chassis, string? Make, string? Model, int? Year,
    byte? PlateType = null, string? PlateTwoDigit = null, string? PlateLetter = null,
    string? PlateThreeDigit = null, string? PlateIranCode = null,
    string? EngineNumber = null, string? VehicleType = null, int? ManufactureYear = null);

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
    bool IsRenewal,
    /// <summary>What the insurer pays the agency — docs/TASKS.md Task 15's P&amp;L income line.
    /// Optional: a policy can be issued before this rate is known and backfilled later.</summary>
    decimal? AgencyCommissionPercent = null,
    /// <summary>docs/TASK-24-POLICY-NUMBER.md §2 — set when the number came from the "ورود دستی
    /// شمارهٔ کامل" escape hatch instead of the locked line/agency/year segments.</summary>
    bool PnManualEntry = false,
    /// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §2/§3 — captured here so a manually-entered
    /// customer's IsProfileComplete can actually become true from this form, instead of always
    /// landing in the completion queue for fields the issuance form never asked for.</summary>
    string? CustomerFirstName = null,
    string? CustomerLastName = null,
    string? CustomerEmergencyMobile = null,
    string? CustomerAddress = null,
    string? CustomerPostalCode = null,
    /// <summary>The wizard's step-3 choice: "cash" or "installment". Cash settles via
    /// record-full-payment; an EXPLICIT "installment" arms the portal verification chain that
    /// hard-blocks the down payment until it completes. Omitted → installment without the block
    /// (legacy/import-era behavior; the import pipeline doesn't use this endpoint).</summary>
    string? PaymentType = null,
    /// <summary>Which insurer this policy was issued through — multi-insurer agencies need it to
    /// split the pending-remittance liability per insurer. Omitted → the agency's own insurer
    /// (Organization.InsurerName).</summary>
    string? InsurerName = null);

public sealed record CreatePolicyResultDto(Guid PolicyId, string PolicyNumber, Guid CustomerId);

/// <summary>docs/TASK-24-POLICY-NUMBER.md §2/§3 — everything the issuance form needs to render the
/// locked segments and a live composed preview, without generating the official number itself.</summary>
public sealed record PolicyNumberSuggestionDto(
    string InsurerName,
    string Separator,
    string? LineCode,
    string? AgencyCode,
    string YearDisplay,
    int SerialLength,
    string SuggestedSerial,
    string? LastSerial,
    DateOnly? LastIssueDate,
    string? ComposedPreview,
    bool CanCompose);

/// <summary>docs/TASK-24-POLICY-NUMBER.md §6 — always non-blocking; an empty list means either no
/// mismatch or the number couldn't be parsed at all (nothing to compare).</summary>
public sealed record PolicyNumberWarningsDto(IReadOnlyList<string> Warnings);
