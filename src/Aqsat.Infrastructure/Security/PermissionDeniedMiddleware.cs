using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Aqsat.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace Aqsat.Infrastructure.Security;

/// <summary>
/// Records one PermissionDenied security event per 403 response on /api paths. Registered BEFORE
/// UseAuthorization/ScopeResolution in Program.cs — middleware nesting follows registration
/// order, so this wrapper observes every 403 source as the response unwinds back out through it:
/// [Authorize] policy denials, scope rejections, maintenance mode. Non-API paths (static files)
/// never produce 403s worth a row. Awaited, not fire-and-forget: one insert on an already-rejected
/// request adds negligible latency, and determinism (tests read the row right after the response)
/// beats the microseconds.
/// </summary>
public sealed class PermissionDeniedMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SecurityEventWriter securityEvents)
    {
        await next(context);

        if (context.Response.StatusCode != StatusCodes.Status403Forbidden ||
            !context.Request.Path.StartsWithSegments("/api"))
        {
            return;
        }

        var sub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        await securityEvents.WriteAsync(
            SecurityEventType.PermissionDenied,
            SecuritySeverity.Warning,
            $"دسترسی غیرمجاز به {context.Request.Path}{(sub is null ? null : $" توسط کاربر {sub}")}");
    }
}
