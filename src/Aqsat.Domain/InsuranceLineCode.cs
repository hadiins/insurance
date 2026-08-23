using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>docs/TASK-24-POLICY-NUMBER.md §4.1 — how an insurer's numeric line code ("1110") maps
/// to our InsuranceLine, per agency (a code an agency uses for one insurer may mean something else
/// for another).</summary>
public class InsuranceLineCode : AgencyOwnedEntity
{
    public Guid InsuranceLineId { get; set; }
    public InsuranceLine InsuranceLine { get; set; } = default!;

    public string InsurerName { get; set; } = default!;
    public string Code { get; set; } = default!;
    public bool IsActive { get; set; } = true;
}
