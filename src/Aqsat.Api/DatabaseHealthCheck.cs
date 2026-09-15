using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aqsat.Api;

/// <summary>
/// /health must mean "the app can actually serve", not just "the process is listening" — a
/// replica that lost its database should stop receiving traffic. Opens a real connection and
/// runs SELECT 1; no RLS-scoped table is touched, so no agency scope is needed.
/// </summary>
public sealed class DatabaseHealthCheck(AppDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync(cancellationToken);
            }
            finally
            {
                await connection.CloseAsync();
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Technical detail (exception) stays in the health payload/secure log; /health has no
            // end-user-facing Persian copy by design — it's read by monitors, not people.
            return HealthCheckResult.Unhealthy("پایگاه داده در دسترس نیست.", ex);
        }
    }
}
