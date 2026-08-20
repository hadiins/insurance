using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>Rates differ per insurance line (docs/PHASE-1-SPEC.md §2.2). Historical rows are never
/// updated in place — a rate change is a new row with a new EffectiveFrom, so past
/// CommissionEntry.RatePercent values (locked at issuance) stay meaningful.</summary>
public class MarketerRate : AgencyOwnedEntity
{
    public Guid MarketerId { get; set; }
    public Marketer Marketer { get; set; } = default!;

    public Guid InsuranceLineId { get; set; }
    public InsuranceLine InsuranceLine { get; set; } = default!;

    public decimal RatePercent { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}
