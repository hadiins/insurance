using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInsurerRemittance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InsurerRemittances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    MethodType = table.Column<byte>(type: "tinyint", nullable: false),
                    CashBoxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferenceNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsurerRemittances", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_InsurerRemittances_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InsurerRemittances_CashBoxes_CashBoxId",
                        column: x => x.CashBoxId,
                        principalTable: "CashBoxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InsurerRemittanceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    InsurerRemittanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstallmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsurerRemittanceLines", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_InsurerRemittanceLines_Installments_InstallmentId",
                        column: x => x.InstallmentId,
                        principalTable: "Installments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InsurerRemittanceLines_InsurerRemittances_InsurerRemittanceId",
                        column: x => x.InsurerRemittanceId,
                        principalTable: "InsurerRemittances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InsurerRemittanceLines_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InsurerRemittanceLines_AgencyId",
                table: "InsurerRemittanceLines",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_InsurerRemittanceLines_AgencyId_InsurerRemittanceId",
                table: "InsurerRemittanceLines",
                columns: new[] { "AgencyId", "InsurerRemittanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InsurerRemittanceLines_AgencyId_PolicyId",
                table: "InsurerRemittanceLines",
                columns: new[] { "AgencyId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_InsurerRemittanceLines_BizId",
                table: "InsurerRemittanceLines",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_InsurerRemittanceLines_InstallmentId",
                table: "InsurerRemittanceLines",
                column: "InstallmentId",
                unique: true,
                filter: "[InstallmentId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InsurerRemittanceLines_InsurerRemittanceId",
                table: "InsurerRemittanceLines",
                column: "InsurerRemittanceId");

            migrationBuilder.CreateIndex(
                name: "IX_InsurerRemittanceLines_PolicyId",
                table: "InsurerRemittanceLines",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_InsurerRemittances_AgencyId",
                table: "InsurerRemittances",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_InsurerRemittances_AgencyId_Date",
                table: "InsurerRemittances",
                columns: new[] { "AgencyId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_InsurerRemittances_BankAccountId",
                table: "InsurerRemittances",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_InsurerRemittances_BizId",
                table: "InsurerRemittances",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_InsurerRemittances_CashBoxId",
                table: "InsurerRemittances",
                column: "CashBoxId");

            // Joins the same predicate function every AgencyId-carrying table uses (CLAUDE.md rule 10).
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.InsurerRemittances,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.InsurerRemittances AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.InsurerRemittances AFTER UPDATE,
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.InsurerRemittanceLines,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.InsurerRemittanceLines AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.InsurerRemittanceLines AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                DROP FILTER PREDICATE ON dbo.InsurerRemittances,
                DROP BLOCK PREDICATE ON dbo.InsurerRemittances AFTER INSERT,
                DROP BLOCK PREDICATE ON dbo.InsurerRemittances AFTER UPDATE,
                DROP FILTER PREDICATE ON dbo.InsurerRemittanceLines,
                DROP BLOCK PREDICATE ON dbo.InsurerRemittanceLines AFTER INSERT,
                DROP BLOCK PREDICATE ON dbo.InsurerRemittanceLines AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "InsurerRemittanceLines");

            migrationBuilder.DropTable(
                name: "InsurerRemittances");
        }
    }
}
