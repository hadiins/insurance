using Aqsat.Api.Contracts;
using Aqsat.Application.Common;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    JwtTokenService tokenService,
    ICurrentUserContext currentUser,
    SecurityEventWriter securityEvents) : ControllerBase
{
    /// <summary>docs/TASKS.md Task 19 — the one unauthenticated, password-checking endpoint is the
    /// obvious brute-force target, so it gets its own tighter limit on top of the API-wide one.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Mobile) || string.IsNullOrWhiteSpace(request.Password))
        {
            return ValidationProblem("شمارهٔ همراه و رمز عبور الزامی است.");
        }

        // Persian digits, +98/0098 prefixes and stray separators are normal keyboard realities —
        // normalizing here (the same validator every customer-facing mobile field uses) turns them
        // into a login instead of a misleading "wrong mobile or password" support ticket.
        var mobile = MobileNumberValidator.Normalize(request.Mobile);

        // AppUser is one of the identity tables Task 3 exempted from RLS — a login lookup must
        // work before any AgencyId scope exists.
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Mobile == mobile && !u.IsDeleted, ct);

        if (user is null || !user.IsActive || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            // Security feed: every failed attempt by mobile+IP — this is the raw signal the
            // brute-force detector and the owner's security dashboard run on. The reason string is
            // deliberately generic (same one the 401 shows) so the row can't leak which half was
            // wrong.
            await securityEvents.WriteAsync(
                SecurityEventType.FailedLogin, SecuritySeverity.Warning,
                "تلاش ناموفق برای ورود به سامانه", mobile, ct);
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "شمارهٔ همراه یا رمز عبور نادرست است.",
            });
        }

        // A pending self-serve signup (feature 5) or a suspended agency must fail HERE with a
        // message the login page can show — letting the login succeed and bouncing every
        // subsequent request off the middleware's 403 would look like an obscure bug, not a
        // clear state. A user with one active org still logs in normally, and a user with NO
        // membership at all is deliberately left to the middleware's own 403 (e.g. a revoked
        // marketer panel user, whose login must succeed so the panel can answer 403).
        var memberships = await dbContext.UserOrgRoles
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && !m.IsDeleted)
            .Join(dbContext.Organizations.Where(o => !o.IsDeleted),
                m => m.OrganizationId, o => o.Id, (m, o) => new { o.IsActive })
            .ToListAsync(ct);
        if (memberships.Count > 0 && memberships.All(m => !m.IsActive))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "نمایندگی شما هنوز توسط مالک سامانه فعال نشده است.",
            });
        }

        var token = tokenService.CreateToken(user);
        await securityEvents.WriteAsync(
            SecurityEventType.SuccessfulLogin, SecuritySeverity.Info,
            $"ورود موفق: {user.FullName}", user.Mobile, ct);
        return Ok(new LoginResponse(token, ExpiresInMinutes: 480));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken ct)
    {
        var memberships = await dbContext.UserOrgRoles
            .AsNoTracking()
            .Where(m => m.UserId == currentUser.UserId && !m.IsDeleted)
            .Include(m => m.Organization)
            .Include(m => m.Role)
            .OrderBy(m => m.OrganizationId)
            .Select(m => new OrganizationMembership(m.OrganizationId, m.Organization.Name, m.Role.Name))
            .ToListAsync(ct);

        return Ok(new MeResponse(
            currentUser.UserId,
            currentUser.DisplayName,
            currentUser.ActiveOrganizationId,
            currentUser.Permissions.ToList(),
            memberships));
    }

    /// <summary>Self-service — any logged-in user (owner, agency manager, staff, or a marketer with
    /// a login) changes their own password. Requires the current password, same as any other
    /// account-security-sensitive action; there is no separate "admin resets someone else's
    /// password" endpoint yet.</summary>
    [HttpPut("change-password")]
    [Authorize]
    public async Task<ActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            return ValidationProblem("رمز عبور جدید باید حداقل ۸ کاراکتر باشد.");
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentUser.UserId, ct);
        if (user is null)
        {
            return NotFound();
        }

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return ValidationProblem("رمز عبور فعلی نادرست است.");
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        await dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
