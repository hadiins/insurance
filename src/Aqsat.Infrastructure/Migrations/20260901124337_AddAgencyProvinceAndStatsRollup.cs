using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgencyProvinceAndStatsRollup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Province",
                table: "Organizations",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgencyStatsDaily",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StatDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PoliciesIssued = table.Column<int>(type: "int", nullable: false),
                    SmsSentCount = table.Column<int>(type: "int", nullable: false),
                    SmsCostToman = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    InquiryPaymentsCount = table.Column<int>(type: "int", nullable: false),
                    InquiryRevenueToman = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    InquiryCallsCount = table.Column<int>(type: "int", nullable: false),
                    InquiryCallCostToman = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyStatsDaily", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_AgencyStatsDaily_Organizations_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Policies_AgencyId_IssueDate",
                table: "Policies",
                columns: new[] { "AgencyId", "IssueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Level_Name",
                table: "Organizations",
                columns: new[] { "Level", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Level_Province",
                table: "Organizations",
                columns: new[] { "Level", "Province" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyStatsDaily_AgencyId_StatDate",
                table: "AgencyStatsDaily",
                columns: new[] { "AgencyId", "StatDate" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyStatsDaily_BizId",
                table: "AgencyStatsDaily",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgencyStatsDaily");

            migrationBuilder.DropIndex(
                name: "IX_Policies_AgencyId_IssueDate",
                table: "Policies");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_Level_Name",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_Level_Province",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Province",
                table: "Organizations");
        }
    }
}
