using Aqsat.Api.Contracts;
using Aqsat.Application.Common;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    [HttpPost("login")]
    [AllowAnonymous]
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

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
