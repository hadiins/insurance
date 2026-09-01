using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SplitAgencyGatewayAndSmsPanel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-preserving column move: the inquiry fee is now an owner-account concern
            // (PlatformPaymentSettings.InquiryFeeToman — collected through the owner's gateway),
            // so each agency's previously configured fee is carried over before the old column
            // drops. MAX over non-default values: one fee serves every agency through the one
            // owner gateway; agencies that never changed it (25000 default) contribute nothing.
            migrationBuilder.AddColumn<decimal>(
                name: "InquiryFeeToman",
                table: "PlatformPaymentSettings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE PlatformPaymentSettings
                SET InquiryFeeToman = ISNULL((
                    SELECT MAX(s.PortalInquiryFeeToman) FROM OrgSettings s
                ), 25000)
                WHERE InquiryFeeToman = 0;
                """);

            migrationBuilder.DropColumn(
                name: "PortalInquiryFeeToman",
                table: "OrgSettings");

            migrationBuilder.AddColumn<string>(
                name: "SmsApiKey",
                table: "OrgSettings",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InquiryFeeToman",
                table: "PlatformPaymentSettings");

            migrationBuilder.DropColumn(
                name: "SmsApiKey",
                table: "OrgSettings");

            migrationBuilder.AddColumn<decimal>(
                name: "PortalInquiryFeeToman",
                table: "OrgSettings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
