using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgencyCommissionRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgencyCommissionRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    InsuranceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyCommissionRates", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_AgencyCommissionRates_InsuranceLines_InsuranceLineId",
                        column: x => x.InsuranceLineId,
                        principalTable: "InsuranceLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyCommissionRates_AgencyId",
                table: "AgencyCommissionRates",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyCommissionRates_AgencyId_InsuranceLineId",
                table: "AgencyCommissionRates",
                columns: new[] { "AgencyId", "InsuranceLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyCommissionRates_BizId",
                table: "AgencyCommissionRates",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_AgencyCommissionRates_InsuranceLineId",
                table: "AgencyCommissionRates",
                column: "InsuranceLineId");

            // Joins the same predicate function every AgencyId-carrying table uses (CLAUDE.md rule 10).
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.AgencyCommissionRates,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.AgencyCommissionRates AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.AgencyCommissionRates AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                DROP FILTER PREDICATE ON dbo.AgencyCommissionRates,
                DROP BLOCK PREDICATE ON dbo.AgencyCommissionRates AFTER INSERT,
                DROP BLOCK PREDICATE ON dbo.AgencyCommissionRates AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "AgencyCommissionRates");
        }
    }
}
