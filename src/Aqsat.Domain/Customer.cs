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

    /// <summary>SHA-256 hash for lookup without decrypting.</summary>
    public byte[]? NationalIdHash { get; set; }

    public string? Mobile { get; set; }
    public DateTimeOffset? MobileVerifiedAt { get; set; }
}
