using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

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
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:NationalIdKey"] = "iNR6AVHkisOPGbBreM0PpHSNmUoom7d0EFVWgcwEdJk=",
            })
            .Build();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(new AgencySessionContextInterceptor())
            .Options;

        return new AppDbContext(options, new AesFieldEncryptor(configuration));
    }
}
