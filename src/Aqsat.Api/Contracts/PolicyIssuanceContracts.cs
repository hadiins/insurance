using System.ComponentModel.DataAnnotations;

namespace Aqsat.Api.Contracts;

public sealed record InsuranceLineDto(
    Guid Id, Guid? ParentId, string Code, string NameFa, bool RequiresVehicle, bool RequiresProperty, int SortOrder);

/// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §5 — Plate is kept for the rare free-text/legacy path,
/// but the issuance form's plate component always sends the four structured parts, from which
/// PlateNormalized is derived server-side (never trusted from the client as a single string).
/// Length caps mirror the Vehicle columns (B14) — an oversized value otherwise reaches SQL Server
/// as a truncation 500 instead of a Persian 400.</summary>
public sealed record VehicleInput(
    [MaxLength(20)] string? Plate,
    [MaxLength(30)] string? Vin,
    [MaxLength(30)] string? Chassis,
    [MaxLength(60)] string? Make,
    [MaxLength(60)] string? Model, int? Year,
    byte? PlateType = null, [MaxLength(2)] string? PlateTwoDigit = null, [MaxLength(10)] string? PlateLetter = null,
    [MaxLength(3)] string? PlateThreeDigit = null, [MaxLength(2)] string? PlateIranCode = null,
    [MaxLength(30)] string? EngineNumber = null, [MaxLength(80)] string? VehicleType = null, int? ManufactureYear = null);

/// <summary>Length caps mirror the PropertySubject columns (B14).</summary>
public sealed record PropertySubjectInput(
    [MaxLength(400)] string Address, [MaxLength(10)] string? PostalCode,
    [MaxLength(60)] string? Type, decimal? Value);

/// <summary>
/// Deliberately excludes DownPayment/InstallmentCount — schedule generation is a separate step
/// (the existing POST /api/policies/{id}/schedule from Task 8), reused as-is rather than
/// duplicated. Field order in the frontend form follows docs/PHASE-1-SPEC.md §5, verbatim: line ->
/// customer -> insured subject -> premium -> service fee -> (down payment/schedule happens next).
/// </summary>
/// <summary>Length caps mirror the Policy/Customer columns (B14) — the guards in PoliciesController
/// catch empty/duplicate numbers, but an oversized string otherwise fails as a SQL truncation 500.</summary>
public sealed record CreatePolicyRequest(
    [MaxLength(40)] string PolicyNumber,
    Guid InsuranceLineId,
    Guid? CustomerId,
    [MaxLength(120)] string? CustomerFullName,
    [MaxLength(15)] string? CustomerMobile,
    [MaxLength(30)] string? CustomerNationalId,
    VehicleInput? Vehicle,
    PropertySubjectInput? Property,
    DateOnly IssueDate,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal NetPremium,
    decimal ServiceFee,
    Guid? MarketerId,
    [MaxLength(120)] string? PreviousInsurer,
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
    [MaxLength(120)] string? InsurerName = null);

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
