using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTodayDashboardSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Payments",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyCollectionGoal",
                table: "OrgSettings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "ImportBatches",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CollectionContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstallmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Channel = table.Column<byte>(type: "tinyint", nullable: false),
                    Outcome = table.Column<byte>(type: "tinyint", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PromisedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    PromisedAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionContacts", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.CheckConstraint("CK_CollectionContacts_Promise", "([PromisedOn] IS NULL AND [PromisedAmount] IS NULL) OR ([PromisedOn] IS NOT NULL AND [PromisedAmount] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CollectionContacts_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionContacts_Installments_InstallmentId",
                        column: x => x.InstallmentId,
                        principalTable: "Installments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionContacts_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionContacts_AgencyId",
                table: "CollectionContacts",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionContacts_AgencyId_CustomerId_OccurredAt",
                table: "CollectionContacts",
                columns: new[] { "AgencyId", "CustomerId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionContacts_AgencyId_InstallmentId",
                table: "CollectionContacts",
                columns: new[] { "AgencyId", "InstallmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionContacts_AgencyId_PolicyId_OccurredAt",
                table: "CollectionContacts",
                columns: new[] { "AgencyId", "PolicyId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionContacts_BizId",
                table: "CollectionContacts",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionContacts_CustomerId",
                table: "CollectionContacts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionContacts_InstallmentId",
                table: "CollectionContacts",
                column: "InstallmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionContacts_PolicyId",
                table: "CollectionContacts",
                column: "PolicyId");

            // Joins the same predicate function every AgencyId-carrying table uses (CLAUDE.md rule
            // 10), same pattern as AddSmsTemplates' own migration.
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CollectionContacts,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CollectionContacts AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CollectionContacts AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                DROP FILTER PREDICATE ON dbo.CollectionContacts,
                DROP BLOCK PREDICATE ON dbo.CollectionContacts AFTER INSERT,
                DROP BLOCK PREDICATE ON dbo.CollectionContacts AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "CollectionContacts");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "MonthlyCollectionGoal",
                table: "OrgSettings");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "ImportBatches");
        }
    }
}
