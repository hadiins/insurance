using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.DataModel;

/// <summary>
/// Points at the dedicated TEST database (via the AQSAT_TEST_CONNECTION environment variable) so
/// RLS tests exercise the real SQL Server security policy, not a fake/in-memory provider that
/// wouldn't have RLS at all — while the suite's table wipes never touch the developer's real
/// `Aqsat` database. The database is created and migrated by <see cref="TestDatabaseCleanup"/> on
/// first run. No connection string with credentials lives in source: set the variable before
/// running, e.g. (PowerShell)
/// <code>$env:AQSAT_TEST_CONNECTION = 'Server=localhost;Database=AqsatTest;User Id=sa;Password=...;TrustServerCertificate=True'</code>
/// </summary>
internal static class TestDbContextFactory
{
    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("AQSAT_TEST_CONNECTION")
        ?? throw new InvalidOperationException(
            "AQSAT_TEST_CONNECTION is not set — the test suite refuses to guess a database and " +
            "must never fall back to the developer's real one.");

    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(new AgencySessionContextInterceptor())
            .Options;

        // No IFieldEncryptor anymore — national IDs are plaintext (owner decision 2026-08-28).
        return new AppDbContext(options);
    }

    /// <summary>Same server/credentials as the test connection, retargeted at another database
    /// (e.g. `master` for backup/restore assertions).</summary>
    public static string ConnectionTo(string databaseName)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = databaseName,
        };
        return builder.ConnectionString;
    }
}
