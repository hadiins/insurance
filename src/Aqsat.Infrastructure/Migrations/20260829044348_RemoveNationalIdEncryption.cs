using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveNationalIdEncryption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CLAUDE.md rule 12 rewrite (owner decision 2026-08-28): national IDs move from AES-GCM
            // varbinary columns to plaintext nvarchar(30). SQL cannot decrypt AES-GCM, so the old
            // bytes are RENAMED to NationalIdEncrypted (kept until NationalIdPlaintextBackfillJob
            // converts them at startup and drops the columns) and fresh plaintext columns are added
            // beside them — a plain AlterColumn would fail on the existing varbinary data anyway.
            //
            // Customers.IsProfileComplete is a PERSISTED computed column over [NationalId], and
            // sp_rename re-binds computed-column definitions to the renamed column — so the computed
            // column and its filtered index are dropped before the rename and rebuilt around the NEW
            // plaintext [NationalId] afterwards.
            migrationBuilder.Sql("DROP INDEX [IX_Customers_AgencyId_IsProfileComplete] ON [Customers];");
            migrationBuilder.Sql("ALTER TABLE [Customers] DROP COLUMN [IsProfileComplete];");

            migrationBuilder.RenameColumn(
                name: "NationalId",
                table: "Customers",
                newName: "NationalIdEncrypted");

            migrationBuilder.AddColumn<string>(
                name: "NationalId",
                table: "Customers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.Sql(
                "ALTER TABLE [Customers] ADD [IsProfileComplete] AS CASE WHEN [NationalId] IS NOT NULL " +
                "AND [Mobile] IS NOT NULL AND [Address] IS NOT NULL AND [PostalCode] IS NOT NULL " +
                "AND [FirstName] IS NOT NULL AND [LastName] IS NOT NULL " +
                "THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END PERSISTED;");
            migrationBuilder.Sql(
                "CREATE INDEX [IX_Customers_AgencyId_IsProfileComplete] ON [Customers] ([AgencyId], [IsProfileComplete]) " +
                "WHERE [IsDeleted] = 0;");

            migrationBuilder.RenameColumn(
                name: "NationalId",
                table: "Marketers",
                newName: "NationalIdEncrypted");

            migrationBuilder.AddColumn<string>(
                name: "NationalId",
                table: "Marketers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            // The issuance wizard's step-1 lookup searches by plaintext national ID (rule 3: AgencyId
            // leads every index).
            migrationBuilder.CreateIndex(
                name: "IX_Customers_AgencyId_NationalId",
                table: "Customers",
                columns: new[] { "AgencyId", "NationalId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_AgencyId_NationalId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "NationalId",
                table: "Marketers");

            migrationBuilder.RenameColumn(
                name: "NationalIdEncrypted",
                table: "Marketers",
                newName: "NationalId");

            migrationBuilder.Sql("DROP INDEX [IX_Customers_AgencyId_IsProfileComplete] ON [Customers];");
            migrationBuilder.Sql("ALTER TABLE [Customers] DROP COLUMN [IsProfileComplete];");

            migrationBuilder.DropColumn(
                name: "NationalId",
                table: "Customers");

            migrationBuilder.RenameColumn(
                name: "NationalIdEncrypted",
                table: "Customers",
                newName: "NationalId");

            migrationBuilder.Sql(
                "ALTER TABLE [Customers] ADD [IsProfileComplete] AS CASE WHEN [NationalId] IS NOT NULL " +
                "AND [Mobile] IS NOT NULL AND [Address] IS NOT NULL AND [PostalCode] IS NOT NULL " +
                "AND [FirstName] IS NOT NULL AND [LastName] IS NOT NULL " +
                "THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END PERSISTED;");
            migrationBuilder.Sql(
                "CREATE INDEX [IX_Customers_AgencyId_IsProfileComplete] ON [Customers] ([AgencyId], [IsProfileComplete]) " +
                "WHERE [IsDeleted] = 0;");
        }
    }
}
