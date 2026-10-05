using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <summary>Owner's master switch for the issuance-verification chain (step 4): a column on the
    /// platform singleton plus a per-policy creation stamp. defaults: switch ON (current behavior —
    /// a fresh install must never boot with the chain silently off), bypass flag OFF.</summary>
    public partial class AddVerificationEnabledSwitch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "VerificationBypassedByOwner",
                table: "Policies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "VerificationEnabled",
                table: "PlatformPaymentSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VerificationBypassedByOwner",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "VerificationEnabled",
                table: "PlatformPaymentSettings");
        }
    }
}
