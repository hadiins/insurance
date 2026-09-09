using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallmentPaymentLinks : Migration
    {
        /// <summary>RLS for the link table (rule 10): same predicate family as migration
        /// AddRowLevelSecurity. PaymentLinkTokenIndex is deliberately NOT added to the policy — it
        /// is the installment-payment portal's one RLS-exempt lookup (token → agency), same shape
        /// and same reason as PortalInvitationTokenIndex in AddCustomerPortalInvitations: SQL
        /// Server applies security-policy predicates even inside scalar function bodies, so an
        /// anonymous visitor cannot be scoped through a function — only through a plain unfiltered
        /// table.</summary>
        private static readonly string[] RlsBatches =
        [
            """
            ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CustomerPaymentLinks,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CustomerPaymentLinks AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CustomerPaymentLinks AFTER UPDATE;
            """,
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentLinkTtlDays",
                table: "OrgSettings",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.CreateTable(
                name: "CustomerPaymentLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(43)", maxLength: 43, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_CustomerPaymentLinks", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_CustomerPaymentLinks_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentLinkTokenIndex",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Token = table.Column<string>(type: "nvarchar(43)", maxLength: 43, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentLinkTokenIndex", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPaymentLinks_AgencyId",
                table: "CustomerPaymentLinks",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPaymentLinks_AgencyId_CustomerId",
                table: "CustomerPaymentLinks",
                columns: new[] { "AgencyId", "CustomerId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPaymentLinks_AgencyId_Token",
                table: "CustomerPaymentLinks",
                columns: new[] { "AgencyId", "Token" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPaymentLinks_BizId",
                table: "CustomerPaymentLinks",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPaymentLinks_CustomerId",
                table: "CustomerPaymentLinks",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLinkTokenIndex_AgencyId_Token",
                table: "PaymentLinkTokenIndex",
                columns: new[] { "AgencyId", "Token" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLinkTokenIndex_BizId",
                table: "PaymentLinkTokenIndex",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLinkTokenIndex_Token",
                table: "PaymentLinkTokenIndex",
                column: "Token",
                unique: true);

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
                    DROP FILTER PREDICATE ON dbo.CustomerPaymentLinks;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP BLOCK PREDICATE ON dbo.CustomerPaymentLinks AFTER INSERT;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP BLOCK PREDICATE ON dbo.CustomerPaymentLinks AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "CustomerPaymentLinks");

            migrationBuilder.DropTable(
                name: "PaymentLinkTokenIndex");

            migrationBuilder.DropColumn(
                name: "PaymentLinkTtlDays",
                table: "OrgSettings");
        }
    }
}
