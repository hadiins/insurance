using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerKindAndPassport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Kind",
                table: "Customers",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PassportExpiry",
                table: "Customers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassportNumber",
                table: "Customers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "PassportNumberHash",
                table: "Customers",
                type: "varbinary(900)",
                nullable: true);

            // A passport-only customer is complete without a national ID (owner decision
            // 2026-09-21): the computed column now accepts either identifier.
            migrationBuilder.AlterColumn<bool>(
                name: "IsProfileComplete",
                table: "Customers",
                type: "bit",
                nullable: false,
                computedColumnSql: "CASE WHEN COALESCE([NationalId], [PassportNumber]) IS NOT NULL AND [Mobile] IS NOT NULL AND [Address] IS NOT NULL AND [PostalCode] IS NOT NULL AND [FirstName] IS NOT NULL AND [LastName] IS NOT NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
                stored: true,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldComputedColumnSql: "CASE WHEN [NationalId] IS NOT NULL AND [Mobile] IS NOT NULL AND [Address] IS NOT NULL AND [PostalCode] IS NOT NULL AND [FirstName] IS NOT NULL AND [LastName] IS NOT NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
                oldStored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_AgencyId_PassportNumberHash",
                table: "Customers",
                columns: new[] { "AgencyId", "PassportNumberHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_AgencyId_PassportNumberHash",
                table: "Customers");

            migrationBuilder.AlterColumn<bool>(
                name: "IsProfileComplete",
                table: "Customers",
                type: "bit",
                nullable: false,
                computedColumnSql: "CASE WHEN [NationalId] IS NOT NULL AND [Mobile] IS NOT NULL AND [Address] IS NOT NULL AND [PostalCode] IS NOT NULL AND [FirstName] IS NOT NULL AND [LastName] IS NOT NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
                stored: true,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldComputedColumnSql: "CASE WHEN COALESCE([NationalId], [PassportNumber]) IS NOT NULL AND [Mobile] IS NOT NULL AND [Address] IS NOT NULL AND [PostalCode] IS NOT NULL AND [FirstName] IS NOT NULL AND [LastName] IS NOT NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
                oldStored: true);

            migrationBuilder.DropColumn(
                name: "PassportNumberHash",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PassportNumber",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PassportExpiry",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Customers");
        }
    }
}
