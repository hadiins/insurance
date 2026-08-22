using Aqsat.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aqsat.Infrastructure.Jobs;

/// <summary>
/// docs/TASKS.md Task 20 — daily backup, plus retention cleanup. Runs a native SQL Server
/// <c>BACKUP DATABASE</c> (server-side — the file lands in SQL Server's own filesystem, so
/// Backup:Directory must be a path the SQL Server process can write to, shared with this API's
/// container via the same volume so the retention sweep below can also see it).
///
/// A backup nobody has ever restored is not a backup (this task's own check) — see
/// docs/RESTORE-RUNBOOK.md for the tested, documented restore procedure.
/// </summary>
public sealed class DatabaseBackupJob(AppDbContext dbContext, IConfiguration configuration, TimeProvider timeProvider, ILogger<DatabaseBackupJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var directory = configuration["Backup:Directory"] ?? "/var/opt/mssql/backup";
        var retentionDays = configuration.GetValue("Backup:RetentionDays", 14);
        var databaseName = dbContext.Database.GetDbConnection().Database;
        var timestamp = timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss");
        var filePath = Path.Combine(directory, $"{databaseName}-{timestamp}.bak").Replace('\\', '/');

        // The database name can't be a query parameter — BACKUP DATABASE takes it as a literal
        // identifier, not an expression position — but it comes from our own connection string, not
        // external input, so building it into the SQL text is safe. The file path IS parameterized.
        // No WITH COMPRESSION: it's an Enterprise/Standard-only feature — SQL Server Express (which
        // LocalDB is built on, and a small agency might run in production too) rejects it outright.
#pragma warning disable EF1002
        await dbContext.Database.ExecuteSqlRawAsync(
            $"BACKUP DATABASE [{databaseName}] TO DISK = @filePath WITH CHECKSUM, INIT;",
            [new SqlParameter("@filePath", filePath)], ct);
#pragma warning restore EF1002

        logger.LogInformation("Database backup written to {FilePath}", filePath);

        CleanupOldBackups(directory, databaseName, retentionDays);
    }

    private void CleanupOldBackups(string directory, string databaseName, int retentionDays)
    {
        if (!Directory.Exists(directory))
        {
            // Filesystem isn't shared with this container (or the very first backup hasn't run
            // yet) — nothing to clean up, and not this job's job to create the directory.
            return;
        }

        var cutoff = timeProvider.GetUtcNow().AddDays(-retentionDays);
        foreach (var file in Directory.EnumerateFiles(directory, $"{databaseName}-*.bak"))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime)
            {
                File.Delete(file);
                logger.LogInformation("Deleted expired backup {FilePath}", file);
            }
        }
    }
}
