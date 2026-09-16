using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations;

/// <inheritdoc />
[Migration("20260908124500_ExemptApiIrCallLogsFromRls")]
public partial class ExemptApiIrCallLogsFromRls : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ApiIrCallLogs is operational cost-reconciliation data written by every api.ir call (Task 14)
        // and read only by Platform.Owner-gated reconciliation. The BLOCK predicate on AgencyId
        // (added in AddReminderAndApiIrLogs) rejected inserts for agency-blind callers: Platform.Owner's
        // session context is NULL but the column is non-nullable, so the predicate `AgencyId = NULL`
        // never matches and no call was ever logged — the table stayed empty after 5 real SendSms
        // attempts. Exempt it from the security policy (same rationale as AgencyStatsDaily): it carries
        // no tenant-private PII, only api.ir call metadata (service, cost, success, timestamp).
        migrationBuilder.Sql(
            """
            ALTER SECURITY POLICY dbo.AgencyAccessPolicy
            DROP FILTER PREDICATE ON dbo.ApiIrCallLogs,
            DROP BLOCK PREDICATE ON dbo.ApiIrCallLogs AFTER INSERT,
            DROP BLOCK PREDICATE ON dbo.ApiIrCallLogs AFTER UPDATE;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER SECURITY POLICY dbo.AgencyAccessPolicy
            ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.ApiIrCallLogs,
            ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.ApiIrCallLogs AFTER INSERT,
            ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.ApiIrCallLogs AFTER UPDATE;
            """);
    }
}
