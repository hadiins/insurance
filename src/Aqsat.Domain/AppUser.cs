using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// A user's identity is global; which agency they can act in comes from UserOrgRole. Not
/// RLS-scoped — a user legitimately needs to be found across the org hierarchy during login/scope
/// resolution, before any single AgencyId session context exists.
/// </summary>
public class AppUser : SoftDeletableEntity
{
    public string FullName { get; set; } = default!;
    public string Mobile { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public bool IsActive { get; set; } = true;
}
