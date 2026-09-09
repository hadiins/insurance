using Aqsat.Domain.Enums;
using Aqsat.Domain.Monitoring;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aqsat.Infrastructure.Security;

/// <summary>
/// Persists one SecurityEvent row on its own scoped AppDbContext — a fresh scope, not the caller's,
/// so it can never accidentally save the request's half-finished tracked changes alongside it. A
/// failure to record a security event must never break the action that triggered it (a failed
/// login that errored instead of returning 401 would lock the user out of their own error), so
/// every failure is logged and swallowed (deliberate exception to rule 15: this is the one place
/// where the primary operation outranks the telemetry).
/// </summary>
public sealed class SecurityEventWriter(
    IServiceScopeFactory scopeFactory, ILogger<SecurityEventWriter> logger)
{
    public async Task WriteAsync(
        SecurityEventType type, SecuritySeverity severity, string detail,
        string? mobile = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.SecurityEvents.Add(new SecurityEvent
            {
                Type = type,
                Severity = severity,
                OccurredAt = DateTimeOffset.UtcNow,
                IpAddress = CurrentRequestContext.IpAddress,
                Mobile = mobile,
                Detail = detail.Length > 500 ? detail[..500] : detail,
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller hung up mid-request — not worth an error line.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ردیف رویداد امنیتی {SecurityEventType} ثبت نشد", type);
        }
    }
}
