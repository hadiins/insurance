using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// docs/PHASE-1-SPEC.md §2.9 — the only part of the product that creates revenue rather than
/// reducing loss. Covers both walk-ins with no policy here yet (ProspectName/ProspectMobile,
/// CustomerId null) and renewals of the agency's own policies (a job creates a watch as
/// Policy.EndDate approaches).
/// </summary>
public class RenewalWatch : AgencyOwnedEntity
{
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string? ProspectName { get; set; }
    public string? ProspectMobile { get; set; }

    public Guid InsuranceLineId { get; set; }
    public InsuranceLine InsuranceLine { get; set; } = default!;

    public string? CurrentInsurer { get; set; }
    public DateOnly CurrentExpiryDate { get; set; }

    /// <summary>"Call me N days before mine expires" — default 2.</summary>
    public int NotifyDaysBefore { get; set; } = 2;

    public Guid? MarketerId { get; set; }
    public Marketer? Marketer { get; set; }

    public RenewalWatchStatus Status { get; set; } = RenewalWatchStatus.Watching;

    /// <summary>Set when it converts.</summary>
    public Guid? PolicyId { get; set; }
    public Policy? Policy { get; set; }
}
