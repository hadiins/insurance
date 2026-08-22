using Microsoft.Data.SqlClient;

namespace Aqsat.Updater.Backup;

/// <summary>
/// docs/UPDATE-SYSTEM.md §3, stage 4 (weight 20%, the largest single stage): a full backup before
/// any migration runs, no exceptions — if this fails, the update does not proceed. Deliberately a
/// plain SqlClient call rather than referencing Aqsat.Infrastructure/EF Core: this service must stay
/// completely decoupled from the main app's assemblies, the same isolation principle that keeps the
/// Docker socket out of Aqsat.Api. The logic mirrors DatabaseBackupJob (src/Aqsat.Infrastructure/Jobs)
/// on purpose — same BACKUP DATABASE statement, independently maintained on each side of the boundary.
/// </summary>
public interface IPreUpdateBackupService
{
    Task<string> BackupAsync(CancellationToken ct);
}

public sealed class PreUpdateBackupService(IConfiguration configuration, ILogger<PreUpdateBackupService> logger) : IPreUpdateBackupService
{
    public async Task<string> BackupAsync(CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        var directory = configuration["Backup:Directory"] ?? "/var/opt/mssql/backup";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var databaseName = connection.Database;
        var filePath = Path.Combine(directory, $"{databaseName}-preupdate-{DateTime.UtcNow:yyyyMMdd-HHmmss}.bak").Replace('\\', '/');

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 600; // A full backup can legitimately take minutes on a large DB.
        command.CommandText = $"BACKUP DATABASE [{databaseName}] TO DISK = @filePath WITH CHECKSUM, INIT;";
        command.Parameters.AddWithValue("@filePath", filePath);
        await command.ExecuteNonQueryAsync(ct);

        logger.LogInformation("Pre-update backup written to {FilePath}", filePath);
        return filePath;
    }
}
