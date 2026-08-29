using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// کاربران و دسترسی‌ها — who can log into this agency and with which role. AppUser/Role/UserOrgRole
/// are the identity tables Task 3 exempted from RLS (CLAUDE.md), so every query here filters by
/// OrganizationId explicitly instead of relying on the database predicate.
/// </summary>
[ApiController]
[Route("api/settings")]
[Authorize(Policy = Permissions.SettingsWrite)]
public sealed class UsersController(AppDbContext dbContext, IPasswordHasher passwordHasher, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<OrgUserDto>>> List(CancellationToken ct)
    {
        var agencyId = currentUser.ActiveOrganizationId;
        var memberships = await dbContext.UserOrgRoles.AsNoTracking()
            .Where(m => m.OrganizationId == agencyId && !m.IsDeleted)
            .Include(m => m.User)
            .Include(m => m.Role)
            .OrderBy(m => m.User.FullName)
            .Select(m => new OrgUserDto(m.Id, m.UserId, m.User.FullName, m.User.Mobile, m.RoleId, m.Role.Name, m.User.IsActive))
            .ToListAsync(ct);

        return Ok(memberships);
    }

    /// <summary>The system "مالک نرم‌افزار" role is deliberately excluded — an agency manager must
    /// never be able to hand Platform.Owner to their own staff through this list (that role only
    /// exists via the one-time owner bootstrap, docs/UPDATE-SYSTEM.md rule 1).</summary>
    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyList<RoleOptionDto>>> Roles(CancellationToken ct)
    {
        var roles = await dbContext.Roles.AsNoTracking()
            .Where(r => !r.IsDeleted && !r.IsSystemRole)
            .OrderBy(r => r.Name)
            .Select(r => new RoleOptionDto(r.Id, r.Name))
            .ToListAsync(ct);

        return Ok(roles);
    }

    /// <summary>An existing user (by mobile) gets a new membership in this agency with the chosen
    /// role; an unseen mobile creates the AppUser first. Matches AppUser's real semantics — identity
    /// is global, org access comes from UserOrgRole (docs/PHASE-1-SPEC.md §2.2).</summary>
    [HttpPost("users")]
    public async Task<ActionResult<OrgUserDto>> Create(CreateOrgUserRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Mobile) || string.IsNullOrWhiteSpace(request.Password))
        {
            return ValidationProblem("نام، شمارهٔ همراه و رمز عبور الزامی است.");
        }

        // Same minimum AuthController.ChangePassword enforces — without this, a manager could
        // create a colleague (or themselves) an account with a one-character password.
        if (request.Password.Length < 8)
        {
            return ValidationProblem("رمز عبور باید حداقل ۸ کاراکتر باشد.");
        }

        var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId && !r.IsDeleted, ct);
        if (role is null || role.IsSystemRole)
        {
            return ValidationProblem("نقش یافت نشد.");
        }

        var agencyId = currentUser.ActiveOrganizationId;
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Mobile == request.Mobile && !u.IsDeleted, ct);

        if (user is null)
        {
            user = new AppUser
            {
                FullName = request.FullName.Trim(),
                Mobile = request.Mobile.Trim(),
                PasswordHash = passwordHasher.Hash(request.Password),
                IsActive = true,
            };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(ct);
        }
        else
        {
            var alreadyMember = await dbContext.UserOrgRoles.AnyAsync(
                m => m.UserId == user.Id && m.OrganizationId == agencyId && !m.IsDeleted, ct);
            if (alreadyMember)
            {
                return ValidationProblem("این کاربر قبلاً در این نمایندگی عضو است.");
            }
        }

        var membership = new UserOrgRole { UserId = user.Id, OrganizationId = agencyId, RoleId = role.Id };
        dbContext.UserOrgRoles.Add(membership);
        await dbContext.SaveChangesAsync(ct);

        return Ok(new OrgUserDto(membership.Id, user.Id, user.FullName, user.Mobile, role.Id, role.Name, user.IsActive));
    }

    /// <summary>Removes this user's access to this agency only — AppUser itself (and its memberships
    /// in other agencies) is untouched, matching "one user, many agencies" (CLAUDE.md).</summary>
    [HttpPut("users/{membershipId:guid}/deactivate")]
    public async Task<ActionResult> Deactivate(Guid membershipId, CancellationToken ct)
    {
        var membership = await dbContext.UserOrgRoles.FirstOrDefaultAsync(
            m => m.Id == membershipId && m.OrganizationId == currentUser.ActiveOrganizationId && !m.IsDeleted, ct);
        if (membership is null)
        {
            return NotFound();
        }

        // Lockout guards: a manager must not be able to remove their own access in one click, and
        // the agency's last remaining membership can never be deactivated (that would permanently
        // lock everyone out with no self-service path back in). Roles here are agency-defined, so
        // "last membership" — not "last manager" — is the enforceable invariant.
        if (membership.UserId == currentUser.UserId)
        {
            return ValidationProblem("حذف دسترسی خودتان مجاز نیست؛ از مدیر دیگری بخواهید این کار را انجام دهد.");
        }

        var remainingMembers = await dbContext.UserOrgRoles.AsNoTracking()
            .CountAsync(m => m.OrganizationId == membership.OrganizationId && !m.IsDeleted && m.Id != membership.Id, ct);
        if (remainingMembers == 0)
        {
            return ValidationProblem("حداقل یک کاربر فعال باید در نمایندگی باقی بماند.");
        }

        membership.IsDeleted = true;
        membership.DeletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
