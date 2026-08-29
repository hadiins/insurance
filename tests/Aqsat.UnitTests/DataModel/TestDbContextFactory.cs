using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.DataModel;

/// <summary>
/// Points at the same LocalDB instance Task 3 migrated (`(localdb)\MSSQLLocalDB`, database
/// `Aqsat`) so RLS tests exercise the real SQL Server security policy, not a fake/in-memory
/// provider that wouldn't have RLS at all.
/// </summary>
internal static class TestDbContextFactory
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=Aqsat;Trusted_Connection=True;TrustServerCertificate=True";

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
