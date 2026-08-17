using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Auth;

/// <summary>
/// Runs between UseAuthentication and UseAuthorization. Resolves which organization the caller is
/// acting in (X-Organization-Id header, or their first org if omitted), reads their permissions for
/// that org fresh from the database — safe to query before AgencyContext is set because
/// UserOrgRole/Role/RolePermission are the identity/hierarchy tables Task 3 exempted from RLS for
/// exactly this reason — then sets AgencyContext, CurrentUserContext, and appends "permission"
/// claims for [Authorize(Policy = ...)] to check. Never returns a silently empty scope (rule 17):
/// no org access at all, or an explicitly requested org the caller doesn't belong to, is a 403.
/// </summary>
public sealed class ScopeResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, AppDbContext dbContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            await next(httpContext);
            return;
        }

        var subClaim = httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (subClaim is null || !Guid.TryParse(subClaim, out var userId))
        {
            await WriteForbiddenAsync(httpContext, "توکن معتبر نیست.");
            return;
        }

        var memberships = await dbContext.UserOrgRoles
            .AsNoTracking()
            .Where(m => m.UserId == userId && !m.IsDeleted)
            .Include(m => m.Organization)
            .Include(m => m.Role).ThenInclude(r => r.RolePermissions)
            .OrderBy(m => m.OrganizationId)
            .ToListAsync(httpContext.RequestAborted);

        if (memberships.Count == 0)
        {
            await WriteForbiddenAsync(httpContext, "این کاربر در هیچ نمایندگی‌ای عضو نیست.");
            return;
        }

        var requestedOrgHeader = httpContext.Request.Headers["X-Organization-Id"].FirstOrDefault();
        var membership = memberships[0];
        if (!string.IsNullOrEmpty(requestedOrgHeader))
        {
            if (!Guid.TryParse(requestedOrgHeader, out var requestedOrgId))
            {
                await WriteForbiddenAsync(httpContext, "شناسهٔ نمایندگی نامعتبر است.");
                return;
            }

            var match = memberships.FirstOrDefault(m => m.OrganizationId == requestedOrgId);
            if (match is null)
            {
                await WriteForbiddenAsync(httpContext, "این کاربر در این نمایندگی عضو نیست.");
                return;
            }

            membership = match;
        }

        var appUser = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, httpContext.RequestAborted);
        if (appUser is null || !appUser.IsActive)
        {
            await WriteForbiddenAsync(httpContext, "این کاربر غیرفعال است.");
            return;
        }

        var permissions = membership.Role.RolePermissions
            .Where(p => !p.IsDeleted)
            .Select(p => p.Permission)
            .ToHashSet();

        CurrentUserContext.Current = new ResolvedUser(userId, appUser.FullName, membership.OrganizationId, permissions);
        AgencyContext.Current = membership.OrganizationId;

        var identity = (ClaimsIdentity)httpContext.User.Identity!;
        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim("permission", permission));
        }

        await next(httpContext);
    }

    private static async Task WriteForbiddenAsync(HttpContext httpContext, string message)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = message,
            Type = "https://httpstatuses.com/403",
            Instance = httpContext.Request.Path,
        };

        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        await httpContext.Response.WriteAsJsonAsync(problemDetails);
    }
}
