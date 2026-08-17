using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportColumnMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportColumnMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ImportType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    MappingJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportColumnMappings", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportColumnMappings_AgencyId",
                table: "ImportColumnMappings",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportColumnMappings_AgencyId_ImportType",
                table: "ImportColumnMappings",
                columns: new[] { "AgencyId", "ImportType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportColumnMappings_BizId",
                table: "ImportColumnMappings",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            // ImportColumnMappings carries AgencyId but was created after AddRowLevelSecurity's
            // CREATE SECURITY POLICY — join the same predicate function via ALTER (CLAUDE.md rule
            // 10 is not optional just because the table came later).
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.ImportColumnMappings,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.ImportColumnMappings AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.ImportColumnMappings AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                DROP FILTER PREDICATE ON dbo.ImportColumnMappings,
                DROP BLOCK PREDICATE ON dbo.ImportColumnMappings AFTER INSERT,
                DROP BLOCK PREDICATE ON dbo.ImportColumnMappings AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "ImportColumnMappings");
        }
    }
}
