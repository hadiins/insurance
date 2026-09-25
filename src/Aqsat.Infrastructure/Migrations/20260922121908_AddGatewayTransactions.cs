using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGatewayTransactions : Migration
    {
        /// <summary>RLS for the gateway-charge table (rule 10): same predicate family as migration
        /// AddRowLevelSecurity — a table money moves through must be invisible outside its own
        /// agency at the database level, not merely at the query level.</summary>
        private static readonly string[] RlsBatches =
        [
            """
            ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.GatewayTransactions,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.GatewayTransactions AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.GatewayTransactions AFTER UPDATE;
            """,
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GatewayTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Purpose = table.Column<byte>(type: "tinyint", nullable: false),
                    GatewayReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MerchantIdUsed = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AmountToman = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    InvitationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InstallmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PaidCardMask = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    BuyerIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayTransactions", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_GatewayTransactions_CustomerPortalInvitations_InvitationId",
                        column: x => x.InvitationId,
                        principalTable: "CustomerPortalInvitations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GatewayTransactions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GatewayTransactions_Installments_InstallmentId",
                        column: x => x.InstallmentId,
                        principalTable: "Installments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GatewayTransactions_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayTransactions_AgencyId",
                table: "GatewayTransactions",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayTransactions_AgencyId_CustomerId",
                table: "GatewayTransactions",
                columns: new[] { "AgencyId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayTransactions_AgencyId_InstallmentId",
                table: "GatewayTransactions",
                columns: new[] { "AgencyId", "InstallmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayTransactions_AgencyId_InvitationId",
                table: "GatewayTransactions",
                columns: new[] { "AgencyId", "InvitationId" });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayTransactions_AgencyId_PolicyId",
                table: "GatewayTransactions",
                columns: new[] { "AgencyId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayTransactions_AgencyId_Provider_GatewayReference",
                table: "GatewayTransactions",
                columns: new[] { "AgencyId", "Provider", "GatewayReference" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayTransactions_AgencyId_Status_CreatedAtUtc",
                table: "GatewayTransactions",
                columns: new[] { "AgencyId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayTransactions_BizId",
                table: "GatewayTransactions",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_GatewayTransactions_CustomerId",
                table: "GatewayTransactions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayTransactions_InstallmentId",
                table: "GatewayTransactions",
                column: "InstallmentId");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayTransactions_InvitationId",
                table: "GatewayTransactions",
                column: "InvitationId");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayTransactions_PolicyId",
                table: "GatewayTransactions",
                column: "PolicyId");

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
                    DROP FILTER PREDICATE ON dbo.GatewayTransactions;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP BLOCK PREDICATE ON dbo.GatewayTransactions AFTER INSERT;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP BLOCK PREDICATE ON dbo.GatewayTransactions AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "GatewayTransactions");
        }
    }
}
