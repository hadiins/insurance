using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>Role is separate from organization — a user may hold different roles in different orgs.</summary>
public class Role : SoftDeletableEntity
{
    public string Name { get; set; } = default!;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
