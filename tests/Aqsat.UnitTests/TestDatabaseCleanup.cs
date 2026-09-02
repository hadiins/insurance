using System;
using System.Runtime.CompilerServices;
using Aqsat.Infrastructure.Persistence;
using Aqsat.UnitTests.DataModel;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests;

/// <summary>
/// The test suite shares one persistent database and every fixture seeder only ever INSERTs. Across
/// many runs the Roles/Users/Organizations (and their child) tables accumulate into the thousands,
/// so queries like RoleManagement's List then scan tens of thousands of rows and blow past the
/// 30s command timeout. Every WebApplicationFactory bootstrap reseeds fresh data via DevSeeder,
/// so wiping the accumulation-prone tables once, before any test runs, is safe and idempotent.
///
/// Since the suite moved to the dedicated `AqsatTest` database, the tables referencing Organizations
/// (AgencyStatsDaily, CustomerPortalInvitations, ApiIrCallLogs) would survive the wipe as orphans
/// and grow unbounded across runs — they are wiped too.
///
/// The wipe is done with RLS (AgencyAccessPolicy) and all FK constraints temporarily disabled:
/// RLS otherwise filters the DELETEs to zero rows (its default predicate denies an unset
/// AgencyContext), and the full FK chain between these tables is deep enough that hand-ordering the
/// deletes is brittle. Everything is re-enabled at the end. This runs exactly once, at assembly
/// load, after creating and migrating the database if it doesn't exist yet (a fresh checkout must
/// be able to run `dotnet test` with no manual database setup).
/// </summary>
internal static class TestDatabaseCleanup
{
    [ModuleInitializer]
    public static void CleanSharedTablesOnce()
    {
        using var context = TestDbContextFactory.Create();
        // Applies every migration (schema + RLS security policy) on an empty database; a no-op
        // when the database is already current.
        context.Database.Migrate();
        // QUOTED_IDENTIFIER is required by the RLS security policy / filtered indexes on this DB.
        context.Database.ExecuteSqlRaw(
            "SET QUOTED_IDENTIFIER ON; " +
            "ALTER SECURITY POLICY AgencyAccessPolicy WITH (STATE = OFF); " +
            "EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL'; " +
            "DELETE FROM CommissionEntries; DELETE FROM MarketerRates; DELETE FROM ReminderLogs; " +
            "DELETE FROM Installments; DELETE FROM RenewalWatches; DELETE FROM Policies; " +
            "DELETE FROM Marketers; DELETE FROM OrgSettings; " +
            "DELETE FROM UserOrgRoles; DELETE FROM RolePermissions; DELETE FROM Roles; " +
            "DELETE FROM Users; DELETE FROM Organizations; DELETE FROM ApiIrSettings; " +
            "DELETE FROM AgencyStatsDaily; DELETE FROM CustomerPortalInvitations; " +
            "DELETE FROM CreditReports; DELETE FROM Banks; " +
            "DELETE FROM PortalInvitationTokenIndex; DELETE FROM ApiIrCallLogs; " +
            "EXEC sp_MSforeachtable 'ALTER TABLE ? CHECK CONSTRAINT ALL'; " +
            "ALTER SECURITY POLICY AgencyAccessPolicy WITH (STATE = ON);");
    }
}
