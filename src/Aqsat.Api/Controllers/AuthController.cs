using Aqsat.Api.Contracts;
using Aqsat.Application.Common;
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
    ICurrentUserContext currentUser) : ControllerBase
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

        // AppUser is one of the identity tables Task 3 exempted from RLS — a login lookup must
        // work before any AgencyId scope exists.
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Mobile == request.Mobile && !u.IsDeleted, ct);

        if (user is null || !user.IsActive || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "شمارهٔ همراه یا رمز عبور نادرست است.",
            });
        }

        var token = tokenService.CreateToken(user);
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
