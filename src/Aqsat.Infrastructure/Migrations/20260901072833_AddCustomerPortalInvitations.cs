using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPortalInvitations : Migration
    {
        /// <summary>RLS for the invitation table (rule 10): same predicate family as migration
        /// AddRowLevelSecurity. PortalInvitationTokenIndex is deliberately NOT added to the
        /// policy — it is the public portal's one RLS-exempt lookup (token → agency) so an
        /// anonymous visitor can be scoped before any RLS read. SQL Server applies
        /// security-policy predicates even inside scalar function bodies, so a
        /// fn_...Agency-style helper fails under a NULL session context — a plain unfiltered
        /// table is the only working shape.</summary>
        private static readonly string[] RlsBatches =
        [
            """
            ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CustomerPortalInvitations,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CustomerPortalInvitations AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CustomerPortalInvitations AFTER UPDATE;
            """,
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerPortalInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(43)", maxLength: 43, nullable: false),
                    InquiryFeeToman = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    PaidAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaidAmountToman = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPortalInvitations", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_CustomerPortalInvitations_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PortalInvitationTokenIndex",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Token = table.Column<string>(type: "nvarchar(43)", maxLength: 43, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvitationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalInvitationTokenIndex", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPortalInvitations_AgencyId",
                table: "CustomerPortalInvitations",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPortalInvitations_AgencyId_CustomerId",
                table: "CustomerPortalInvitations",
                columns: new[] { "AgencyId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPortalInvitations_AgencyId_Token",
                table: "CustomerPortalInvitations",
                columns: new[] { "AgencyId", "Token" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPortalInvitations_BizId",
                table: "CustomerPortalInvitations",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPortalInvitations_CustomerId",
                table: "CustomerPortalInvitations",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PortalInvitationTokenIndex_AgencyId_Token",
                table: "PortalInvitationTokenIndex",
                columns: new[] { "AgencyId", "Token" });

            migrationBuilder.CreateIndex(
                name: "IX_PortalInvitationTokenIndex_BizId",
                table: "PortalInvitationTokenIndex",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_PortalInvitationTokenIndex_Token",
                table: "PortalInvitationTokenIndex",
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
                    DROP FILTER PREDICATE ON dbo.CustomerPortalInvitations;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP BLOCK PREDICATE ON dbo.CustomerPortalInvitations AFTER INSERT;

                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP BLOCK PREDICATE ON dbo.CustomerPortalInvitations AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "CustomerPortalInvitations");

            migrationBuilder.DropTable(
                name: "PortalInvitationTokenIndex");
        }
    }
}
