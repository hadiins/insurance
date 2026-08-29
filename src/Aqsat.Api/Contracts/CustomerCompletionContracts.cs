namespace Aqsat.Api.Contracts;

/// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §3 — the dashboard widget's two counts, since a
/// customer missing only mobile (SMS won't work) and one missing only national ID (Shahkar won't
/// work) call for different fixes.</summary>
public sealed record IncompleteProfileSummaryDto(int Total, int WithoutMobile, int WithoutNationalId);

/// <summary>National ID is masked here (CLAUDE.md rule 12) — the completion grid lets an agent
/// fill in a MISSING value, not read an existing one back.</summary>
public sealed record CustomerIncompleteRowDto(
    Guid Id, string FullName, string? FirstName, string? LastName,
    bool HasNationalId, string? Mobile, string? EmergencyMobile, string? Address, string? PostalCode, bool IsProfileComplete);

/// <summary>All fields optional — a row can be completed across more than one save (§3's
/// "ذخیرهٔ خودکار هر ردیف"). Only fields actually present here are validated and written.</summary>
public sealed record CompleteCustomerProfileRequest(
    string? FirstName, string? LastName, string? NationalId,
    string? Mobile, string? EmergencyMobile, string? Address, string? PostalCode);

/// <summary>One profile row for the issuance wizard's step-1 lookup (GET /api/customers/lookup).
/// The national ID travels in FULL: CLAUDE.md rule 12 was rewritten by owner decision 2026-08-28 —
/// it is plaintext at rest and shown unmasked everywhere, because it is the wizard's entry key, not
/// a secret to hide.</summary>
public sealed record CustomerLookupProfileDto(
    Guid Id, string FullName, string? FirstName, string? LastName, string? NationalId,
    string? Mobile, string? EmergencyMobile, string? Address, string? PostalCode, bool IsProfileComplete);

/// <summary>Found=false for a valid-but-unknown ID (the wizard then offers inline new-customer
/// registration); PolicyCount lets the UI surface "این مشتری N بیمهنامه دارد" before issuing
/// another one.</summary>
public sealed record CustomerLookupResultDto(bool Found, CustomerLookupProfileDto? Customer, int PolicyCount);
