using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicyVerificationChain : Migration
    {
        /// <summary>RLS for the credit-report table (rule 10) — same predicate family as migration
        /// AddRowLevelSecurity. A report is pure agency-scoped data (unlike the token index), so it
        /// joins the policy like every other AgencyId table.</summary>
        private static readonly string[] RlsBatches =
        [
            """
            ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CreditReports,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CreditReports AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CreditReports AFTER UPDATE;
            """,
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresVerification",
                table: "Policies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InstallmentContractText",
                table: "OrgSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AgencyDecisionAtUtc",
                table: "CustomerPortalInvitations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AgencyDecisionByUserId",
                table: "CustomerPortalInvitations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CustomerApprovedAtUtc",
                table: "CustomerPortalInvitations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DownPaymentAmountToman",
                table: "CustomerPortalInvitations",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DownPaymentPaidAtUtc",
                table: "CustomerPortalInvitations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PolicyId",
                table: "CustomerPortalInvitations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Stage",
                table: "CustomerPortalInvitations",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateTable(
                name: "CreditReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChequeCount = table.Column<int>(type: "int", nullable: true),
                    ChequeSumAmountToman = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ChequeSumBouncedAmountToman = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ActiveLoansCount = table.Column<int>(type: "int", nullable: true),
                    LoanTotalAmountToman = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LoanDebtTotalAmountToman = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LoanPastExpiredTotalAmountToman = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LoanDeferredTotalAmountToman = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LoanSuspiciousTotalAmountToman = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LoanDishonoredAmountToman = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RawSuccess = table.Column<bool>(type: "bit", nullable: false),
                    RetrievedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditReports", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_CreditReports_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditReports_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPortalInvitations_AgencyId_PolicyId",
                table: "CustomerPortalInvitations",
                columns: new[] { "AgencyId", "PolicyId" },
                filter: "[PolicyId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPortalInvitations_PolicyId",
                table: "CustomerPortalInvitations",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditReports_AgencyId",
                table: "CreditReports",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditReports_AgencyId_CustomerId",
                table: "CreditReports",
                columns: new[] { "AgencyId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditReports_AgencyId_PolicyId",
                table: "CreditReports",
                columns: new[] { "AgencyId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditReports_BizId",
                table: "CreditReports",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditReports_CustomerId",
                table: "CreditReports",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditReports_PolicyId",
                table: "CreditReports",
                column: "PolicyId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerPortalInvitations_Policies_PolicyId",
                table: "CustomerPortalInvitations",
                column: "PolicyId",
                principalTable: "Policies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

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
                    DROP FILTER PREDICATE ON dbo.CreditReports;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP BLOCK PREDICATE ON dbo.CreditReports AFTER INSERT;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP BLOCK PREDICATE ON dbo.CreditReports AFTER UPDATE;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerPortalInvitations_Policies_PolicyId",
                table: "CustomerPortalInvitations");

            migrationBuilder.DropTable(
                name: "CreditReports");

            migrationBuilder.DropIndex(
                name: "IX_CustomerPortalInvitations_AgencyId_PolicyId",
                table: "CustomerPortalInvitations");

            migrationBuilder.DropIndex(
                name: "IX_CustomerPortalInvitations_PolicyId",
                table: "CustomerPortalInvitations");

            migrationBuilder.DropColumn(
                name: "RequiresVerification",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "InstallmentContractText",
                table: "OrgSettings");

            migrationBuilder.DropColumn(
                name: "AgencyDecisionAtUtc",
                table: "CustomerPortalInvitations");

            migrationBuilder.DropColumn(
                name: "AgencyDecisionByUserId",
                table: "CustomerPortalInvitations");

            migrationBuilder.DropColumn(
                name: "CustomerApprovedAtUtc",
                table: "CustomerPortalInvitations");

            migrationBuilder.DropColumn(
                name: "DownPaymentAmountToman",
                table: "CustomerPortalInvitations");

            migrationBuilder.DropColumn(
                name: "DownPaymentPaidAtUtc",
                table: "CustomerPortalInvitations");

            migrationBuilder.DropColumn(
                name: "PolicyId",
                table: "CustomerPortalInvitations");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "CustomerPortalInvitations");
        }
    }
}
