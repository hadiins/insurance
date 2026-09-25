using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixApiIrCallLogsRlsExemption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 20260920112707_ReExemptApiIrCallLogsFromRls was recorded as applied on production,
            // yet its guarded drop never fired: the guard tested OBJECT_NAME(pp.object_id), which
            // is the SECURITY POLICY's name (AgencyAccessPolicy), not the target table's — the
            // target table lives in pp.target_object_id. The predicate therefore survived and the
            // next anonymous SmsOTP call still lost its call-log row to error 33504 (hardened into
            // a warning by ApiIrClient, but cost accounting must actually work). Same guard as
            // before, fixed column, so an already-exempt database is a no-op.
            // The ALTER goes through EXEC because T-SQL validates ALTER SECURITY POLICY ... DROP at
            // batch-compile time: a static ALTER inside the never-taken IF branch still raises
            // 33261 when the predicate is already gone (e.g. on a fresh database where the earlier
            // 20260908124500_ExemptApiIrCallLogsFromRls dropped it first). Dynamic SQL defers
            // compilation until after the guard's decision.
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM sys.security_predicates pp
                    JOIN sys.security_policies sp ON sp.object_id = pp.object_id
                    WHERE OBJECT_NAME(pp.target_object_id) = 'ApiIrCallLogs')
                BEGIN
                    EXEC(N'ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                        DROP FILTER PREDICATE ON dbo.ApiIrCallLogs,
                        DROP BLOCK PREDICATE ON dbo.ApiIrCallLogs AFTER INSERT,
                        DROP BLOCK PREDICATE ON dbo.ApiIrCallLogs AFTER UPDATE;');
                END
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
}
