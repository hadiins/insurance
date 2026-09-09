namespace Aqsat.Api.Contracts;

/// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §3 — the completion widget's counts. The original two
/// (mobile/national ID) explain which feature breaks; the three added 2026-09-07 after a
/// production incident (a national ID stored as lastName) cover every remaining required field, so
/// a "1 incomplete, 0 and 0" widget never hides what is actually missing.</summary>
public sealed record IncompleteProfileSummaryDto(
    int Total, int WithoutMobile, int WithoutNationalId,
    int WithoutAddress, int WithoutPostalCode, int WithoutName);

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

/// <summary>POST /api/customers — registering a brand-new customer BEFORE any policy exists, so a
/// pre-issuance credit-check portal link can be sent on the very first visit (owner decision
/// 2026-09-03). NationalId and Mobile are required here — unlike the issuance form's inline
/// registration, this endpoint exists specifically to buy inquiries and send an SMS.</summary>
public sealed record CreateCustomerRequest(
    string? FirstName, string? LastName, string? NationalId, string? Mobile,
    string? EmergencyMobile, string? PostalCode, string? Address);
