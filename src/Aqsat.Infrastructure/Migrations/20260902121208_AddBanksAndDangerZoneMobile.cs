using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBanksAndDangerZoneMobile : Migration
    {
        /// <summary>RLS for the banks table (rule 10) — same predicate family as every other
        /// AgencyId table.</summary>
        private static readonly string[] RlsBatches =
        [
            """
            ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.Banks,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.Banks AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.Banks AFTER UPDATE;
            """,
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DangerZoneManagerMobile",
                table: "OrgSettings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Banks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Banks", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Banks_AgencyId",
                table: "Banks",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Banks_AgencyId_Name",
                table: "Banks",
                columns: new[] { "AgencyId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Banks_BizId",
                table: "Banks",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            foreach (var batch in RlsBatches)
            {
                migrationBuilder.Sql(batch);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP FILTER PREDICATE ON dbo.Banks;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP BLOCK PREDICATE ON dbo.Banks AFTER INSERT;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP BLOCK PREDICATE ON dbo.Banks AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "Banks");

            migrationBuilder.DropColumn(
                name: "DangerZoneManagerMobile",
                table: "OrgSettings");
        }
    }
}
