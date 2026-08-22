using System.Security.Cryptography;
using System.Text;

namespace Aqsat.Updater.Auth;

/// <summary>
/// docs/UPDATE-SYSTEM.md §1: "only accepts requests from Aqsat.Api, via mTLS or a shared token."
/// This service takes the shared-token path — simpler to operate than a full mTLS cert chain, and
/// the token only ever needs to protect an internal-network-only service that never has a published
/// port (docker-compose.prod.yml). Every request, including GET /status, requires it — nothing here
/// is meant to be reachable by anyone other than the main API.
/// </summary>
public sealed class SharedTokenAuthMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var expectedToken = configuration["Updater:SharedToken"];
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Updater:SharedToken is not configured." });
            return;
        }

        var providedToken = context.Request.Headers["X-Updater-Token"].FirstOrDefault();
        if (providedToken is null || !FixedTimeEquals(providedToken, expectedToken))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing X-Updater-Token." });
            return;
        }

        await next(context);
    }

    /// <summary>Ordinary string equality short-circuits on the first mismatched byte, which leaks
    /// timing information an attacker could use to guess the token one byte at a time. This never
    /// short-circuits based on content.</summary>
    private static bool FixedTimeEquals(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
}
