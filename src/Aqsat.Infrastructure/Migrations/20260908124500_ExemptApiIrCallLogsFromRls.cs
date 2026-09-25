using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations;

/// <inheritdoc />
// DbContextAttribute is required: without it EF Core never associates this migration with
// AppDbContext, so it is invisible to `migrations list` / Migrate() and the history row is
// never written (the file existed while every database sat at 36/37 applied). The Up body is
// guarded below so re-discovery is safe on databases that already reached the exempt state.
[DbContext(typeof(AppDbContext))]
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
        // Guarded drop: databases that already reached the exempt state (via the later
        // 20260920122734_FixApiIrCallLogsRlsExemption, a manual fix, or a restored backup whose
        // history row for this migration went missing) hit error 33261 — "does not contain a
        // predicate" — on the unguarded ALTER, which broke context.Database.Migrate() downstream
        // (test-suite module initializer, MigrateOnStartup). Same corrected guard as the Fix
        // migration: test pp.target_object_id (the target table), not pp.object_id (the policy).
        // Already-exempt → no-op so the migration can be recorded; still-exempt → drops, the intent.
        //
        // The ALTER goes through EXEC: T-SQL validates ALTER SECURITY POLICY ... DROP at
        // BATCH-COMPILE time, so a static ALTER inside the never-taken IF branch still raises
        // 33261 when the predicate is absent (verified: `IF 1=0 BEGIN ALTER ... END` fails
        // identically). Dynamic SQL is compiled only when the guard lets it execute.
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
