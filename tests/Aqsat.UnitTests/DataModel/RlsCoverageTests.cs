using Aqsat.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.DataModel;

/// <summary>
/// The app's tenant isolation is 100% database-side RLS; membership in dbo.AgencyAccessPolicy is
/// hand-maintained SQL at the bottom of each migration, so a new AgencyId-bearing table whose
/// author forgets that ALTER SECURITY POLICY would silently expose cross-tenant rows in every
/// controller. This test closes that loop: every entity type in the EF model carrying an AgencyId
/// property must have a FILTER predicate under the policy. The carve-outs are deliberate,
/// documented RLS exemptions, not oversights:
/// - PortalInvitationTokenIndex — the public portal's token→agency resolver (anonymous visitors
///   hold no session context); it carries no tenant data beyond AgencyId itself.
/// - PaymentLinkTokenIndex — same model for the installment-payment link's /pay/{token} page:
///   the anonymous resolver resolves token→agency→link; no tenant data beyond AgencyId itself.
/// - AgencyStatsDaily — the owner-platform's pre-aggregated reporting rollup. It is populated by
///   the nightly AgencyStatsRollupJob (which enters each agency's scope to compute it) and read
///   only by Platform.Owner-gated controllers; agency users never query it.
/// - NetworkRiskProfiles / NetworkRiskPlateIndex — the cross-agency risk-sharing tables (Phase
///   2B-1): cross-agency reads are the feature itself. They are gated by the Platform.Owner
///   RiskNetworkSettings switch plus the Risk.NetworkRead permission instead, and hold status-only
///   rows keyed by the keyed HMAC NationalIdHash (no amounts, no identity) written in every
///   assessment's own transaction.
/// </summary>
public class RlsCoverageTests
{
    /// <summary>Deliberately NOT under the RLS policy — see the class summary. Adding a table
    /// here requires an explicit security justification, not an oversight.</summary>
    private static readonly string[] RlsExemptTables =
        ["PortalInvitationTokenIndex", "PaymentLinkTokenIndex", "AgencyStatsDaily", "NetworkRiskProfiles", "NetworkRiskPlateIndex"];

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

        var exemptTables = RlsExemptTables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uncovered = agencyScopedTables
            .Except(coveredTables.Concat(exemptTables))
            .OrderBy(t => t)
            .ToList();
        Assert.True(
            uncovered.Count == 0,
            $"Tables with an AgencyId column missing from dbo.AgencyAccessPolicy (cross-tenant leak): {string.Join(", ", uncovered)}. " +
            "Add FILTER + BLOCK predicates for them in a migration.");
    }
}
