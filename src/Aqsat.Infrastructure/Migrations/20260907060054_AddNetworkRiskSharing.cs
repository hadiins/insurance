using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNetworkRiskSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NetworkRiskPlateIndex",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlateNormalized = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NationalIdHash = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetworkRiskPlateIndex", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_NetworkRiskPlateIndex_Organizations_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NetworkRiskProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NationalIdHash = table.Column<byte[]>(type: "varbinary(900)", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    RiskLevel = table.Column<byte>(type: "tinyint", nullable: false),
                    Decision = table.Column<byte>(type: "tinyint", nullable: false),
                    OverdueCount = table.Column<int>(type: "int", nullable: false),
                    MaxDaysOverdue = table.Column<int>(type: "int", nullable: false),
                    ReturnedChequeCount = table.Column<int>(type: "int", nullable: false),
                    OnTimeRatePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TenureMonths = table.Column<int>(type: "int", nullable: false),
                    SettledInstallmentCount = table.Column<int>(type: "int", nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetworkRiskProfiles", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_NetworkRiskProfiles_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NetworkRiskProfiles_Organizations_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiskNetworkSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskNetworkSettings", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NetworkRiskPlateIndex_AgencyId_PlateNormalized",
                table: "NetworkRiskPlateIndex",
                columns: new[] { "AgencyId", "PlateNormalized" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkRiskPlateIndex_BizId",
                table: "NetworkRiskPlateIndex",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_NetworkRiskPlateIndex_PlateNormalized",
                table: "NetworkRiskPlateIndex",
                column: "PlateNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkRiskProfiles_AgencyId_NationalIdHash",
                table: "NetworkRiskProfiles",
                columns: new[] { "AgencyId", "NationalIdHash" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkRiskProfiles_BizId",
                table: "NetworkRiskProfiles",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_NetworkRiskProfiles_CustomerId",
                table: "NetworkRiskProfiles",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkRiskProfiles_NationalIdHash",
                table: "NetworkRiskProfiles",
                column: "NationalIdHash");

            migrationBuilder.CreateIndex(
                name: "IX_RiskNetworkSettings_BizId",
                table: "RiskNetworkSettings",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NetworkRiskPlateIndex");

            migrationBuilder.DropTable(
                name: "NetworkRiskProfiles");

            migrationBuilder.DropTable(
                name: "RiskNetworkSettings");
        }
    }
}
