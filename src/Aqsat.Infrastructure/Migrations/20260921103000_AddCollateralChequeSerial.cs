using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <summary>
    /// Collateral.ChequeSerial (سریال صیادی چک) exists on the entity and in the model snapshot
    /// but no body migration in this chain ever created the column, so any INSERT into
    /// Collaterals failed with "Invalid column name 'ChequeSerial'" on a freshly-migrated
    /// database. The column is added conditionally: a database that already received it through
    /// another worktree's migration chain must not fail on this one (idempotent ADD/DROP).
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260921103000_AddCollateralChequeSerial")]
    public partial class AddCollateralChequeSerial : Migration
    {
        protected override void Up(MigrationBuilder db)
        {
            db.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[Collaterals]') AND name = N'ChequeSerial')
    ALTER TABLE [dbo].[Collaterals] ADD [ChequeSerial] nvarchar(20) NULL;");
        }

        protected override void Down(MigrationBuilder db)
        {
            db.Sql(@"
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'[dbo].[Collaterals]') AND name = N'ChequeSerial')
    ALTER TABLE [dbo].[Collaterals] DROP COLUMN [ChequeSerial];");
        }
    }
}
