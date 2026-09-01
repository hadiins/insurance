using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// Self-referencing so Agency/Regional/Headquarters all fit from day one, even though only Agency
/// ships in Phase 1. It is the tenant itself, so it does not carry AgencyId and is not RLS-scoped.
/// </summary>
public class Organization : SoftDeletableEntity
{
    public Guid? ParentId { get; set; }
    public Organization? Parent { get; set; }

    public OrganizationLevel Level { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Province { get; set; }
    public string? City { get; set; }
    public string? InsurerName { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>docs/TASK-24-POLICY-NUMBER.md §4.3 — the code embedded in the official policy
    /// number (e.g. "576210"), distinct from <see cref="Code"/> which is this org's own internal
    /// reference. Locked at the service layer once the agency's first policy exists — changing it
    /// would make every prior policy number meaningless.</summary>
    public string? AgencyCode { get; set; }
}
