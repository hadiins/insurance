using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// Singleton row (at most one) holding the api.ir credential and the paid-endpoint switch —
/// platform-level exactly like UpdatePackage: no AgencyId, not RLS-scoped. Editing api.ir's key or
/// flipping billed endpoints used to require a .env edit plus a container recreate; this table makes
/// it a panel setting (Platform.Owner-only, same rule as the update panel). ApiIrClient falls back
/// to ApiIrOptions (env/config) for every field this row leaves unset, so a fresh install behaves
/// exactly as before the table existed.
/// </summary>
public class ApiIrSettings : SoftDeletableEntity
{
    /// <summary>api.ir's bearer key. Empty = fall back to ApiIrOptions.ApiKey from configuration.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Null = fall back to ApiIrOptions.AllowPaidEndpoints (false by default — sandbox).
    /// An explicit true/false here always wins over the configured value; CLAUDE.md's
    /// "deliberate, visible configuration change" now happens in the panel instead of a shell.</summary>
    public bool? AllowPaidEndpoints { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedByUserId { get; set; }
}