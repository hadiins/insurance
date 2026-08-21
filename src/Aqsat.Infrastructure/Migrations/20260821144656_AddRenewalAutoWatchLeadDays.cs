using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRenewalAutoWatchLeadDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RenewalAutoWatchLeadDays",
                table: "OrgSettings",
                type: "int",
                nullable: false,
                defaultValue: 60);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RenewalAutoWatchLeadDays",
                table: "OrgSettings");
        }
    }
}
