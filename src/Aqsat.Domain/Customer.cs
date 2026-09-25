using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// No free-text field about a person, ever — that is the entire legal defence of the product.
/// A Notes column here is a bug (CLAUDE.md rule 8).
/// </summary>
public class Customer : AgencyOwnedEntity
{
    /// <summary>Which identity document this customer was registered with (owner decision
    /// 2026-09-21). Defaults to Iranian for every row that predates the column.</summary>
    public CustomerKind Kind { get; set; } = CustomerKind.Iranian;

    /// <summary>Fanavaran internal code — the join key in Phase 1.</summary>
    public string ExternalCode { get; set; } = default!;

    public string FullName { get; set; } = default!;

    /// <summary>
    /// Plaintext in memory; encrypted at rest via an EF value converter (Aqsat.Infrastructure).
    /// Nullable — not present in every import.
    /// </summary>
    [AuditSensitive]
    public string? NationalId { get; set; }

    /// <summary>HMAC-SHA256 hash (keyed with a server-side secret) for lookup without decrypting.
    /// Deliberately keyed — an unkeyed hash of a 10-digit national ID is reversible by precomputation.</summary>
    public byte[]? NationalIdHash { get; set; }

    /// <summary>Passport number for <see cref="CustomerKind.ForeignPassportOnly"/> customers (and
    /// optionally recorded for ForeignResident ones too). Plaintext, normalized to uppercase Latin,
    /// so the file page can show it and the wizard can look a customer up by it.</summary>
    public string? PassportNumber { get; set; }

    /// <summary>Same keyed-HMAC scheme as <see cref="NationalIdHash"/> — dedupe and cross-agency
    /// paths must never rely on plaintext equality of an identifier.</summary>
    public byte[]? PassportNumberHash { get; set; }

    /// <summary>Passport expiry (Gregorian) — displayed Jalali in the UI; no deadline logic yet,
    /// a stale-passport warning belongs to a later task.</summary>
    public DateOnly? PassportExpiry { get; set; }

    public string? Mobile { get; set; }
    public DateTimeOffset? MobileVerifiedAt { get; set; }

    // docs/TASK-25-IDENTITY-VEHICLE.md §2/§7 — FullName is split going forward; the original value
    // is preserved in FullNameLegacy rather than destroyed, and FirstName/LastName are populated by
    // a data step in step 5, not by this schema change.
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FullNameLegacy { get; set; }

    /// <summary>Deliberately must never equal <see cref="Mobile"/> — see §2's rule.</summary>
    public string? EmergencyMobile { get; set; }

    public string? Address { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>Persisted computed column (SQL-generated, §3) — never set from C#. The private
    /// setter is EF's hook for materializing query results, not a real write path.</summary>
    public bool IsProfileComplete { get; private set; }
}
