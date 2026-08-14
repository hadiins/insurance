using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <summary>
    /// CLAUDE.md rule 10: Row-Level Security in SQL Server via CREATE SECURITY POLICY + a
    /// predicate function on AgencyId — not a repository Where(x => x.AgencyId == ...). If a
    /// developer forgets an application-level filter, the database still refuses cross-tenant
    /// rows. The predicate reads SESSION_CONTEXT('AgencyId'), stamped on every connection by
    /// AgencySessionContextInterceptor.
    /// </summary>
    public partial class AddRowLevelSecurity : Migration
    {
        // Every table that carries an AgencyId column (13 total): the 10 AgencyOwnedEntity
        // tables plus AuditEntries, RecordPresences and RecordLocks, which have AgencyId without
        // following the full Entity/AgencyOwnedEntity CLR shape.
        private static readonly string[] AgencyScopedTables =
        [
            "Customers", "Vehicles", "ContractTemplates", "Policies", "Installments",
            "Payments", "PaymentAllocations", "Collaterals", "ImportBatches", "ImportRows",
            "AuditEntries", "RecordPresences", "RecordLocks",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE FUNCTION dbo.fn_AgencyAccessPredicate(@AgencyId uniqueidentifier)
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                RETURN SELECT 1 AS fn_result
                WHERE @AgencyId = CAST(SESSION_CONTEXT(N'AgencyId') AS uniqueidentifier);
                """);

            var predicateClauses = string.Join(
                ",\n",
                AgencyScopedTables.SelectMany(table => new[]
                {
                    $"ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.{table}",
                    $"ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.{table} AFTER INSERT",
                    $"ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.{table} AFTER UPDATE",
                }));

            migrationBuilder.Sql(
                $"""
                CREATE SECURITY POLICY dbo.AgencyAccessPolicy
                {predicateClauses}
                WITH (STATE = ON);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY dbo.AgencyAccessPolicy;");
            migrationBuilder.Sql("DROP FUNCTION dbo.fn_AgencyAccessPredicate;");
        }
    }
}
