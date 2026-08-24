using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>The agency's own commission rate from the insurer — per insurance line (including
/// sub-lines, since InsuranceLine is self-referencing and each sub-line can carry a different
/// rate). Mirrors MarketerRate's shape/versioning: a rate change is a new row with a new
/// EffectiveFrom, so past Policy.AgencyCommissionPercent values (locked at issuance) stay
/// meaningful.</summary>
public class AgencyCommissionRate : AgencyOwnedEntity
{
    public Guid InsuranceLineId { get; set; }
    public InsuranceLine InsuranceLine { get; set; } = default!;

    public decimal RatePercent { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}
