using Aqsat.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.DataModel;

/// <summary>
/// The app's tenant isolation is 100% database-side RLS; membership in dbo.AgencyAccessPolicy is
/// hand-maintained SQL at the bottom of each migration, so a new AgencyId-bearing table whose
/// author forgets that ALTER SECURITY POLICY would silently expose cross-tenant rows in every
/// controller. This test closes that loop: every entity type in the EF model carrying an AgencyId
/// property must have a FILTER predicate under the policy — no exceptions, no allowlist.
/// </summary>
public class RlsCoverageTests
{
    [Fact]
    public async Task Every_entity_with_an_AgencyId_property_is_covered_by_the_RLS_policy()
    {
        await using var context = TestDbContextFactory.Create();

        var agencyScopedTables = context.Model.GetEntityTypes()
            .Where(e => e.FindProperty("AgencyId") is not null)
            .Select(e => e.GetTableName()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(agencyScopedTables);

        var coveredTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        // sys.security_policy_predicates doesn't exist on the LocalDB edition, but the policy's
        // object dependencies do: every FILTER/BLOCK binding shows up as a reference from
        // dbo.AgencyAccessPolicy to its target table (3 rows per table, hence DISTINCT).
        command.CommandText = """
            SELECT DISTINCT referenced_entity_name
            FROM sys.dm_sql_referenced_entities('dbo.AgencyAccessPolicy', 'OBJECT')
            WHERE referenced_entity_name IN (SELECT name FROM sys.tables);
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            coveredTables.Add(reader.GetString(0));
        }

        Assert.NotEmpty(coveredTables);

        var uncovered = agencyScopedTables.Except(coveredTables).OrderBy(t => t).ToList();
        Assert.True(
            uncovered.Count == 0,
            $"Tables with an AgencyId column missing from dbo.AgencyAccessPolicy (cross-tenant leak): {string.Join(", ", uncovered)}. " +
            "Add FILTER + BLOCK predicates for them in a migration.");
    }
}
