using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// No free-text field about a person, ever — that is the entire legal defence of the product.
/// A Notes column here is a bug (CLAUDE.md rule 8).
/// </summary>
public class Customer : AgencyOwnedEntity
{
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
