using Aqsat.Application.Platform;
using Microsoft.AspNetCore.Mvc;

namespace Aqsat.Api.Middleware;

/// <summary>
/// docs/UPDATE-SYSTEM.md §7: "new requests → maintenance page with an estimate. Platform.Owner
/// still has access." Runs after ScopeResolutionMiddleware (needs the resolved permission claims)
/// but exempts a short allow-list even for anonymous callers: health checks (monitoring must not
/// start reporting the whole app down), auth endpoints (the owner has to be able to log in to prove
/// they're the owner), the platform panel's own API (so it can keep polling progress), the SPA
/// shell and its static assets (so the frontend can render a maintenance screen instead of a
/// browser's raw connection-failed page), and the SignalR hub (the maintenance warning itself is
/// delivered over it).
/// </summary>
public sealed class MaintenanceModeMiddleware(RequestDelegate next, IMaintenanceModeService maintenanceMode)
{
    private static readonly string[] ExemptPrefixes =
    [
        "/health", "/api/auth/", "/api/platform/", "/hubs/",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isApiRequest = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
        var isExempt = ExemptPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (!maintenanceMode.IsActive || !isApiRequest || isExempt)
        {
            await next(context);
            return;
        }

        var isPlatformOwner = context.User.HasClaim("permission", "Platform.Owner");
        if (isPlatformOwner)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "سامانه در حال به‌روزرسانی است. لطفاً چند دقیقهٔ دیگر تلاش کنید.",
            Extensions = { ["estimatedEndsAt"] = maintenanceMode.EstimatedEndsAt },
        });
    }
}
