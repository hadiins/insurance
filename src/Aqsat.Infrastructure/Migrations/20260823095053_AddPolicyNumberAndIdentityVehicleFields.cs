using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicyNumberAndIdentityVehicleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EngineNumber",
                table: "Vehicles",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManufactureYear",
                table: "Vehicles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlateIranCode",
                table: "Vehicles",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlateLetter",
                table: "Vehicles",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlateNormalized",
                table: "Vehicles",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlateThreeDigit",
                table: "Vehicles",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlateTwoDigit",
                table: "Vehicles",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "PlateType",
                table: "Vehicles",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<string>(
                name: "VehicleType",
                table: "Vehicles",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PnAgencyCode",
                table: "Policies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PnIsParsed",
                table: "Policies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PnLineCode",
                table: "Policies",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PnManualEntry",
                table: "Policies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PnParseNote",
                table: "Policies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PnSerial",
                table: "Policies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PnYear",
                table: "Policies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgencyCode",
                table: "Organizations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Customers",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyMobile",
                table: "Customers",
                type: "nvarchar(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "Customers",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FullNameLegacy",
                table: "Customers",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "Customers",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Customers",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsProfileComplete",
                table: "Customers",
                type: "bit",
                nullable: false,
                computedColumnSql: "CASE WHEN [NationalId] IS NOT NULL AND [Mobile] IS NOT NULL AND [Address] IS NOT NULL AND [PostalCode] IS NOT NULL AND [FirstName] IS NOT NULL AND [LastName] IS NOT NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
                stored: true);

            migrationBuilder.CreateTable(
                name: "InsuranceLineCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    InsuranceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InsurerName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
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
                    table.PrimaryKey("PK_InsuranceLineCodes", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_InsuranceLineCodes_InsuranceLines_InsuranceLineId",
                        column: x => x.InsuranceLineId,
                        principalTable: "InsuranceLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PolicyNumberFormats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    InsurerName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Pattern = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Separator = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    LineCodeLength = table.Column<int>(type: "int", nullable: false),
                    AgencyCodeLength = table.Column<int>(type: "int", nullable: false),
                    YearDigits = table.Column<int>(type: "int", nullable: false),
                    SerialLength = table.Column<int>(type: "int", nullable: false),
                    IsStrict = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_PolicyNumberFormats", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_AgencyId_PlateNormalized",
                table: "Vehicles",
                columns: new[] { "AgencyId", "PlateNormalized" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_AgencyId_PnYear_PnSerial",
                table: "Policies",
                columns: new[] { "AgencyId", "PnYear", "PnSerial" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_AgencyId_IsProfileComplete",
                table: "Customers",
                columns: new[] { "AgencyId", "IsProfileComplete" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InsuranceLineCodes_AgencyId",
                table: "InsuranceLineCodes",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_InsuranceLineCodes_AgencyId_InsurerName_Code",
                table: "InsuranceLineCodes",
                columns: new[] { "AgencyId", "InsurerName", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InsuranceLineCodes_AgencyId_InsurerName_InsuranceLineId",
                table: "InsuranceLineCodes",
                columns: new[] { "AgencyId", "InsurerName", "InsuranceLineId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InsuranceLineCodes_BizId",
                table: "InsuranceLineCodes",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_InsuranceLineCodes_InsuranceLineId",
                table: "InsuranceLineCodes",
                column: "InsuranceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyNumberFormats_AgencyId",
                table: "PolicyNumberFormats",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyNumberFormats_AgencyId_InsurerName",
                table: "PolicyNumberFormats",
                columns: new[] { "AgencyId", "InsurerName" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyNumberFormats_BizId",
                table: "PolicyNumberFormats",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            // Both new tables carry AgencyId — join the same predicate function every
            // AgencyId-carrying table uses (CLAUDE.md rule 10), same pattern as every migration
            // that has added a table since AddRowLevelSecurity.
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.InsuranceLineCodes,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.InsuranceLineCodes AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.InsuranceLineCodes AFTER UPDATE,
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.PolicyNumberFormats,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.PolicyNumberFormats AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.PolicyNumberFormats AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                DROP FILTER PREDICATE ON dbo.InsuranceLineCodes,
                DROP BLOCK PREDICATE ON dbo.InsuranceLineCodes AFTER INSERT,
                DROP BLOCK PREDICATE ON dbo.InsuranceLineCodes AFTER UPDATE,
                DROP FILTER PREDICATE ON dbo.PolicyNumberFormats,
                DROP BLOCK PREDICATE ON dbo.PolicyNumberFormats AFTER INSERT,
                DROP BLOCK PREDICATE ON dbo.PolicyNumberFormats AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "InsuranceLineCodes");

            migrationBuilder.DropTable(
                name: "PolicyNumberFormats");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_AgencyId_PlateNormalized",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Policies_AgencyId_PnYear_PnSerial",
                table: "Policies");

            migrationBuilder.DropIndex(
                name: "IX_Customers_AgencyId_IsProfileComplete",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IsProfileComplete",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EngineNumber",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "ManufactureYear",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "PlateIranCode",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "PlateLetter",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "PlateNormalized",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "PlateThreeDigit",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "PlateTwoDigit",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "PlateType",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "VehicleType",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "PnAgencyCode",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "PnIsParsed",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "PnLineCode",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "PnManualEntry",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "PnParseNote",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "PnSerial",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "PnYear",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "AgencyCode",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EmergencyMobile",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FullNameLegacy",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Customers");
        }
    }
}
