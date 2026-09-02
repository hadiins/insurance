using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountingImprovements : Migration
    {
        /// <summary>RLS for the two new tables (rule 10) — same predicate family as migration
        /// AddRowLevelSecurity. Both are pure agency-scoped money-flow tables.</summary>
        private static readonly string[] RlsBatches =
        [
            """
            ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.FundTransfers,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.FundTransfers AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.FundTransfers AFTER UPDATE;
            """,
            """
            ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CommissionPayouts,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CommissionPayouts AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CommissionPayouts AFTER UPDATE;
            """,
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InsurerName",
                table: "Policies",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalance",
                table: "CashBoxes",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalance",
                table: "BankAccounts",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "CommissionPayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    MarketerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
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
                    table.PrimaryKey("PK_CommissionPayouts", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_CommissionPayouts_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionPayouts_CashBoxes_CashBoxId",
                        column: x => x.CashBoxId,
                        principalTable: "CashBoxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionPayouts_Marketers_MarketerId",
                        column: x => x.MarketerId,
                        principalTable: "Marketers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FundTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FromCashBoxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FromBankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToCashBoxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToBankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundTransfers", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_FundTransfers_BankAccounts_FromBankAccountId",
                        column: x => x.FromBankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FundTransfers_BankAccounts_ToBankAccountId",
                        column: x => x.ToBankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FundTransfers_CashBoxes_FromCashBoxId",
                        column: x => x.FromCashBoxId,
                        principalTable: "CashBoxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FundTransfers_CashBoxes_ToCashBoxId",
                        column: x => x.ToCashBoxId,
                        principalTable: "CashBoxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_AgencyId",
                table: "CommissionPayouts",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_AgencyId_BankAccountId",
                table: "CommissionPayouts",
                columns: new[] { "AgencyId", "BankAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_AgencyId_CashBoxId",
                table: "CommissionPayouts",
                columns: new[] { "AgencyId", "CashBoxId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_AgencyId_MarketerId",
                table: "CommissionPayouts",
                columns: new[] { "AgencyId", "MarketerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_AgencyId_PaidOn",
                table: "CommissionPayouts",
                columns: new[] { "AgencyId", "PaidOn" });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_BankAccountId",
                table: "CommissionPayouts",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_BizId",
                table: "CommissionPayouts",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_CashBoxId",
                table: "CommissionPayouts",
                column: "CashBoxId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_MarketerId",
                table: "CommissionPayouts",
                column: "MarketerId");

            migrationBuilder.CreateIndex(
                name: "IX_FundTransfers_AgencyId",
                table: "FundTransfers",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_FundTransfers_AgencyId_Date",
                table: "FundTransfers",
                columns: new[] { "AgencyId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_FundTransfers_AgencyId_FromBankAccountId",
                table: "FundTransfers",
                columns: new[] { "AgencyId", "FromBankAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_FundTransfers_AgencyId_FromCashBoxId",
                table: "FundTransfers",
                columns: new[] { "AgencyId", "FromCashBoxId" });

            migrationBuilder.CreateIndex(
                name: "IX_FundTransfers_AgencyId_ToBankAccountId",
                table: "FundTransfers",
                columns: new[] { "AgencyId", "ToBankAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_FundTransfers_AgencyId_ToCashBoxId",
                table: "FundTransfers",
                columns: new[] { "AgencyId", "ToCashBoxId" });

            migrationBuilder.CreateIndex(
                name: "IX_FundTransfers_BizId",
                table: "FundTransfers",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_FundTransfers_FromBankAccountId",
                table: "FundTransfers",
                column: "FromBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FundTransfers_FromCashBoxId",
                table: "FundTransfers",
                column: "FromCashBoxId");

            migrationBuilder.CreateIndex(
                name: "IX_FundTransfers_ToBankAccountId",
                table: "FundTransfers",
                column: "ToBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FundTransfers_ToCashBoxId",
                table: "FundTransfers",
                column: "ToCashBoxId");

            foreach (var batch in RlsBatches)
            {
                migrationBuilder.Sql(batch);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP FILTER PREDICATE ON dbo.FundTransfers;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP BLOCK PREDICATE ON dbo.FundTransfers AFTER INSERT;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP BLOCK PREDICATE ON dbo.FundTransfers AFTER UPDATE;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP FILTER PREDICATE ON dbo.CommissionPayouts;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP BLOCK PREDICATE ON dbo.CommissionPayouts AFTER INSERT;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP BLOCK PREDICATE ON dbo.CommissionPayouts AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "CommissionPayouts");

            migrationBuilder.DropTable(
                name: "FundTransfers");

            migrationBuilder.DropColumn(
                name: "InsurerName",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "OpeningBalance",
                table: "CashBoxes");

            migrationBuilder.DropColumn(
                name: "OpeningBalance",
                table: "BankAccounts");
        }
    }
}
