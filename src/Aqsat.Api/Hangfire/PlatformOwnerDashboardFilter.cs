using Aqsat.Application.Auth;
using Hangfire.Dashboard;

namespace Aqsat.Api.Hangfire;

/// <summary>
/// Replaces Hangfire's default LocalRequestsOnlyAuthorizationFilter: a loopback-IP check is no
/// authorization at all — any SSRF from inside the container, sidecar, or same-host proxy would
/// pass it and gain full job management (retries, deletions, enqueues). This filter instead
/// requires an authenticated caller whose ScopeResolutionMiddleware-resolved claims carry
/// Platform.Owner — the same bar the update panel itself enforces. Note this replaces the default
/// filter entirely (Hangfire applies defaults only when none are supplied), so /hangfire is now
/// unreachable without a valid owner JWT regardless of source IP.
/// </summary>
public sealed class PlatformOwnerDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        return httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.Claims.Any(c => c.Type == "permission" && c.Value == Permissions.PlatformOwner);
    }
}
