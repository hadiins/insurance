using Aqsat.Application.Auth;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Seed;

/// <summary>
/// The seeded "بازاریاب" system role — the only way an agency manager can hand a marketer the
/// Marketer.SelfView permission, since role definitions are platform-level and only the platform
/// owner may create them (RoleManagementController). IsSystemRole guards it from editing/deletion
/// through that controller, and get-or-create by Name keeps every-startup runs safe (same spirit
/// as InsuranceLineSeeder).
/// </summary>
public static class MarketerRoleSeeder
{
    public const string RoleName = "بازاریاب";

    public static async Task<Role> EnsureSeededAsync(AppDbContext context, CancellationToken ct = default)
    {
        var role = await context.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Name == RoleName && !r.IsDeleted, ct);
        if (role is not null)
        {
            return role;
        }

        role = new Role { Name = RoleName, IsSystemRole = true };
        context.Roles.Add(role);
        await context.SaveChangesAsync(ct);
        context.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = Permissions.MarketerSelfView });

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Role is global (no AgencyId/RLS) — concurrent callers can race this check-then-insert;
            // the loser's duplicate-key failure still leaves the role seeded (CLAUDE.md rule 24's spirit).
            foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
            {
                entry.State = EntityState.Detached;
            }

            role = await context.Roles
                .Include(r => r.RolePermissions)
                .FirstAsync(r => r.Name == RoleName && !r.IsDeleted, ct);
        }

        return role;
    }
}
