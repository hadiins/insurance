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
