using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>Role is separate from organization — a user may hold different roles in different orgs.</summary>
public class Role : SoftDeletableEntity
{
    public string Name { get; set; } = default!;

    /// <summary>The single auto-created "Platform Owner" role from the owner bootstrap flow —
    /// never editable or deletable through the role-management UI, and never assignable to an
    /// agency's own staff (docs/UPDATE-SYSTEM.md rule 1).</summary>
    public bool IsSystemRole { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
