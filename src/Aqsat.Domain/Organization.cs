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
    public string? City { get; set; }
    public string? InsurerName { get; set; }
    public bool IsActive { get; set; } = true;
}
