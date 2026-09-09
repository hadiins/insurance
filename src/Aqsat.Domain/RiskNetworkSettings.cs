using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// Singleton row (at most one) holding the platform-wide switch for cross-agency risk sharing
/// (owner decision 2026-09-07: automatic and mutual, but only once the platform owner enables
/// it — default OFF). Platform-level exactly like ApiIrSettings: no AgencyId, not RLS-scoped,
/// edited only through a Platform.Owner-gated endpoint. Reads of NetworkRiskProfile are refused
/// while disabled; the derived tables are still written, so flipping the switch takes effect
/// instantly with no backfill.
/// </summary>
public class RiskNetworkSettings : SoftDeletableEntity
{
    public bool IsEnabled { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedByUserId { get; set; }
}
