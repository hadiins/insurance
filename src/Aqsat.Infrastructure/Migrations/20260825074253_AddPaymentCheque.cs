using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCheque : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentCheques",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChequeNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PresenterName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CashBoxId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCheques", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_PaymentCheques_CashBoxes_CashBoxId",
                        column: x => x.CashBoxId,
                        principalTable: "CashBoxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentCheques_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentCheques_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCheques_AgencyId",
                table: "PaymentCheques",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCheques_AgencyId_PolicyId",
                table: "PaymentCheques",
                columns: new[] { "AgencyId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCheques_AgencyId_Status",
                table: "PaymentCheques",
                columns: new[] { "AgencyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCheques_BizId",
                table: "PaymentCheques",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCheques_CashBoxId",
                table: "PaymentCheques",
                column: "CashBoxId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCheques_PaymentId",
                table: "PaymentCheques",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCheques_PolicyId",
                table: "PaymentCheques",
                column: "PolicyId");

            // Joins the same predicate function every AgencyId-carrying table uses (CLAUDE.md rule 10).
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.PaymentCheques,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.PaymentCheques AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.PaymentCheques AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                DROP FILTER PREDICATE ON dbo.PaymentCheques,
                DROP BLOCK PREDICATE ON dbo.PaymentCheques AFTER INSERT,
                DROP BLOCK PREDICATE ON dbo.PaymentCheques AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "PaymentCheques");
        }
    }
}
