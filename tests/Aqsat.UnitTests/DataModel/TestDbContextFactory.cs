using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.DataModel;

/// <summary>
/// Points at the local SQL Server 2022 instance's dedicated TEST database (`localhost`,
/// database `AqsatTest`) so RLS tests exercise the real SQL Server security policy, not a
/// fake/in-memory provider that wouldn't have RLS at all — while the suite's table wipes never
/// touch the developer's real `Aqsat` database. The database is created and migrated by
/// <see cref="TestDatabaseCleanup"/> on first run.
/// </summary>
internal static class TestDbContextFactory
{
    private const string ConnectionString =
        "Server=localhost;Database=AqsatTest;User Id=sa;Password=4Q45BPLZyL8yOWdqCglj;TrustServerCertificate=True";

    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(new AgencySessionContextInterceptor())
            .Options;

        // No IFieldEncryptor anymore — national IDs are plaintext (owner decision 2026-08-28).
        return new AppDbContext(options);
    }
}
