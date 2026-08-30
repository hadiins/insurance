using System;
using System.Runtime.CompilerServices;
using Aqsat.Infrastructure.Persistence;
using Aqsat.UnitTests.DataModel;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests;

/// <summary>
/// The test suite shares one persistent LocalDB and every fixture seeder only ever INSERTs. Across
/// many runs the Roles/Users/Organizations (and their child) tables accumulate into the thousands,
/// so queries like RoleManagement's List then scan tens of thousands of rows on a desktop LocalDB
/// and blow past the 30s command timeout. Every WebApplicationFactory bootstrap reseeds fresh data
/// via DevSeeder, so wiping the accumulation-prone tables once, before any test runs, is safe and
/// idempotent.
///
/// The wipe is done with RLS (AgencyAccessPolicy) and all FK constraints temporarily disabled:
/// RLS otherwise filters the DELETEs to zero rows (its default predicate denies an unset
/// AgencyContext), and the full FK chain between these tables is deep enough that hand-ordering the
/// deletes is brittle. Everything is re-enabled at the end. This runs exactly once, at assembly load.
/// </summary>
internal static class TestDatabaseCleanup
{
    [ModuleInitializer]
    public static void CleanSharedTablesOnce()
    {
        using var context = TestDbContextFactory.Create();
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
            "EXEC sp_MSforeachtable 'ALTER TABLE ? CHECK CONSTRAINT ALL'; " +
            "ALTER SECURITY POLICY AgencyAccessPolicy WITH (STATE = ON);");
    }
}
