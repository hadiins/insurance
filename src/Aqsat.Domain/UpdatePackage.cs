using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// docs/UPDATE-SYSTEM.md §4 — the catalog of signed releases the Platform.Owner can apply. Platform-
/// level, not per-agency: no AgencyId, excluded from RLS (like Organization/AppUser/Role). Yanking a
/// bad release (IsYanked) hides it from the panel without deleting the row — the audit trail of
/// "this version existed and was pulled" matters as much as the release notes themselves.
/// </summary>
public class UpdatePackage : SoftDeletableEntity
{
    public string Version { get; set; } = default!;
    public string ReleaseNotesFa { get; set; } = default!;
    public string ImageTag { get; set; } = default!;
    public string Sha256 { get; set; } = default!;
    public string SignatureBase64 { get; set; } = default!;

    /// <summary>Refuses to offer this package to an instance running an older version than this —
    /// docs/UPDATE-SYSTEM.md's "you cannot jump straight from 1.0 to 3.0" prerequisite check.</summary>
    public string? MinimumFromVersion { get; set; }

    public bool HasDbMigration { get; set; }
    public bool IsSecurityUpdate { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public bool IsYanked { get; set; }
}
