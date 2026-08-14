using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// The join that actually ties a user's role to a specific organization. Deliberately not
/// RLS-scoped: Task 4's login/scope-resolution flow must read a user's rows across every org they
/// belong to before a single AgencyId session context can be established.
/// </summary>
public class UserOrgRole : SoftDeletableEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = default!;

    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = default!;
}
