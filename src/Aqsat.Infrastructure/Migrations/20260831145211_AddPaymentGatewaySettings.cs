using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentGatewaySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentMerchantId",
                table: "OrgSettings",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CustomerPortalEnabled",
                table: "OrgSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PaymentProvider",
                table: "OrgSettings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                // Existing agencies must land on the same default the rest of the system assumes
                // (mock gateway), not an unparseable empty string.
                defaultValue: "Mock");

            migrationBuilder.AddColumn<decimal>(
                name: "PortalInquiryFeeToman",
                table: "OrgSettings",
                type: "decimal(18,2)",
                nullable: false,
                // Mirror OrgSettings' constructor defaults so existing rows read exactly like a
                // fresh save: 25000 toman inquiry fee, 72h link TTL.
                defaultValue: 25000m);

            migrationBuilder.AddColumn<int>(
                name: "PortalInvitationTtlHours",
                table: "OrgSettings",
                type: "int",
                nullable: false,
                defaultValue: 72);

            migrationBuilder.CreateTable(
                name: "PlatformPaymentSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OwnerMerchantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CallbackBaseUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_PlatformPaymentSettings", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformPaymentSettings_BizId",
                table: "PlatformPaymentSettings",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformPaymentSettings");

            migrationBuilder.DropColumn(
                name: "AgentMerchantId",
                table: "OrgSettings");

            migrationBuilder.DropColumn(
                name: "CustomerPortalEnabled",
                table: "OrgSettings");

            migrationBuilder.DropColumn(
                name: "PaymentProvider",
                table: "OrgSettings");

            migrationBuilder.DropColumn(
                name: "PortalInquiryFeeToman",
                table: "OrgSettings");

            migrationBuilder.DropColumn(
                name: "PortalInvitationTtlHours",
                table: "OrgSettings");
        }
    }
}
