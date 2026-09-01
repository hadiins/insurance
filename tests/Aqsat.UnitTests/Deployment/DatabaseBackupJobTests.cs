using Aqsat.Infrastructure.Jobs;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aqsat.UnitTests.Deployment;

/// <summary>
/// Task 20's own check (docs/TASKS.md): "restore a backup into a clean container and confirm the
/// data. A backup never restored is not a backup." Docker isn't available in this environment, so
/// this exercises the identical BACKUP DATABASE / RESTORE DATABASE T-SQL against the same SQL
/// Server engine (LocalDB) a production container would run — a clean database standing in for a
/// clean container. See docs/RESTORE-RUNBOOK.md for the operator-facing procedure this proves out.
/// </summary>
public class DatabaseBackupJobTests
{
    [Fact]
    public async Task A_backup_written_by_the_job_restores_into_a_fresh_database_with_the_same_data()
    {
        await using var context = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(context);
        AgencyContext.Current = fixture.AgencyAId;

        var backupDirectory = Path.Combine(Path.GetTempPath(), $"aqsat-backup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Backup:Directory"] = backupDirectory,
                ["Backup:RetentionDays"] = "14",
            })
            .Build();

        // Scoped to this fixture's own four org rows, not a global COUNT(*) — Organizations grows
        // constantly from every other test's own DevSeeder call running concurrently in the same
        // shared LocalDB, so a global count read here and re-read after restore is inherently racy.
        var fixtureOrgIds = new[] { fixture.HeadquartersId, fixture.RegionalId, fixture.AgencyAId, fixture.AgencyBId };

        var job = new DatabaseBackupJob(context, configuration, TimeProvider.System, NullLogger<DatabaseBackupJob>.Instance);

        try
        {
            await job.RunAsync();

            // The job names backups after the connection's database — AqsatTest here, Aqsat in
            // production — so derive the pattern instead of hardcoding either name.
            var databaseName = context.Database.GetDbConnection().Database;
            var backupFiles = Directory.GetFiles(backupDirectory, $"{databaseName}-*.bak");
            Assert.Single(backupFiles);
            var backupFile = backupFiles[0];
            Assert.True(new FileInfo(backupFile).Length > 0);

            var restoredDbName = $"AqsatRestoreTest_{Guid.NewGuid():N}"[..30];
            const string masterConnectionString =
                "Server=localhost;Database=master;User Id=sa;Password=4Q45BPLZyL8yOWdqCglj;TrustServerCertificate=True";

            await using var masterConnection = new SqlConnection(masterConnectionString);
            await masterConnection.OpenAsync();

            try
            {
                // Discover the logical file names the backup carries — RESTORE needs them to remap
                // physical paths (WITH MOVE), the same discovery step docs/RESTORE-RUNBOOK.md walks
                // an operator through by hand.
                string dataLogicalName;
                string logLogicalName;
                await using (var fileListCommand = masterConnection.CreateCommand())
                {
                    fileListCommand.CommandText = "RESTORE FILELISTONLY FROM DISK = @path;";
                    fileListCommand.Parameters.AddWithValue("@path", backupFile);
                    await using var reader = await fileListCommand.ExecuteReaderAsync();
                    var names = new List<(string LogicalName, string Type)>();
                    while (await reader.ReadAsync())
                    {
                        names.Add((reader.GetString(reader.GetOrdinal("LogicalName")), reader.GetString(reader.GetOrdinal("Type"))));
                    }

                    dataLogicalName = names.First(n => n.Type == "D").LogicalName;
                    logLogicalName = names.First(n => n.Type == "L").LogicalName;
                }

                var dataFilePath = Path.Combine(backupDirectory, $"{restoredDbName}.mdf");
                var logFilePath = Path.Combine(backupDirectory, $"{restoredDbName}.ldf");

                await using (var restoreCommand = masterConnection.CreateCommand())
                {
                    restoreCommand.CommandText =
                        $"RESTORE DATABASE [{restoredDbName}] FROM DISK = @path " +
                        $"WITH MOVE '{dataLogicalName}' TO @dataPath, MOVE '{logLogicalName}' TO @logPath, REPLACE;";
                    restoreCommand.Parameters.AddWithValue("@path", backupFile);
                    restoreCommand.Parameters.AddWithValue("@dataPath", dataFilePath);
                    restoreCommand.Parameters.AddWithValue("@logPath", logFilePath);
                    await restoreCommand.ExecuteNonQueryAsync();
                }

                // Confirm the data — the whole point of the check. RLS's security policy is bound to
                // this specific database, so query the restored copy directly rather than through
                // AppDbContext (which targets the original test database, not the restored one).
                await using var restoredConnection = new SqlConnection(
                    $"Server=localhost;Database={restoredDbName};User Id=sa;Password=4Q45BPLZyL8yOWdqCglj;TrustServerCertificate=True");
                await restoredConnection.OpenAsync();
                await using var countCommand = restoredConnection.CreateCommand();
                countCommand.CommandText = "SELECT COUNT(*) FROM Organizations WHERE Id IN (@id0, @id1, @id2, @id3);";
                for (var i = 0; i < fixtureOrgIds.Length; i++)
                {
                    countCommand.Parameters.AddWithValue($"@id{i}", fixtureOrgIds[i]);
                }

                var restoredFixtureOrgCount = (int)(await countCommand.ExecuteScalarAsync())!;

                Assert.Equal(fixtureOrgIds.Length, restoredFixtureOrgCount);
            }
            finally
            {
                await using var dropCommand = masterConnection.CreateCommand();
                dropCommand.CommandText =
                    $"IF DB_ID('{restoredDbName}') IS NOT NULL BEGIN ALTER DATABASE [{restoredDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{restoredDbName}]; END";
                await dropCommand.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            Directory.Delete(backupDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Retention_cleanup_deletes_only_backups_older_than_the_configured_window()
    {
        await using var context = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(context);
        AgencyContext.Current = fixture.AgencyAId;

        var backupDirectory = Path.Combine(Path.GetTempPath(), $"aqsat-backup-retention-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);

        try
        {
            var databaseName = context.Database.GetDbConnection().Database;
            var oldFile = Path.Combine(backupDirectory, $"{databaseName}-20200101-000000.bak");
            var recentFile = Path.Combine(backupDirectory, $"{databaseName}-20200102-000000.bak");
            await File.WriteAllTextAsync(oldFile, "old");
            await File.WriteAllTextAsync(recentFile, "recent");
            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-30));
            File.SetLastWriteTimeUtc(recentFile, DateTime.UtcNow.AddDays(-1));

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Backup:Directory"] = backupDirectory,
                    ["Backup:RetentionDays"] = "14",
                })
                .Build();
            var job = new DatabaseBackupJob(context, configuration, TimeProvider.System, NullLogger<DatabaseBackupJob>.Instance);

            await job.RunAsync();

            var remaining = Directory.GetFiles(backupDirectory, $"{databaseName}-*.bak").Select(Path.GetFileName).ToList();
            Assert.DoesNotContain(Path.GetFileName(oldFile), remaining);
            Assert.Contains(Path.GetFileName(recentFile), remaining);
            // Plus the fresh backup this run itself just wrote.
            Assert.Equal(2, remaining.Count);
        }
        finally
        {
            Directory.Delete(backupDirectory, recursive: true);
        }
    }
}
