using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderAndApiIrLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiIrCallLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Service = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    CostToman = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    WasSandboxed = table.Column<bool>(type: "bit", nullable: false),
                    CalledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiIrCallLogs", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "ReminderLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    InstallmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RenewalWatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecipientType = table.Column<byte>(type: "tinyint", nullable: false),
                    Mobile = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    OffsetDays = table.Column<int>(type: "int", nullable: false),
                    TemplateKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Channel = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderLogs", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_ReminderLogs_Installments_InstallmentId",
                        column: x => x.InstallmentId,
                        principalTable: "Installments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReminderLogs_RenewalWatches_RenewalWatchId",
                        column: x => x.RenewalWatchId,
                        principalTable: "RenewalWatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiIrCallLogs_AgencyId",
                table: "ApiIrCallLogs",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiIrCallLogs_AgencyId_Service_CalledAt",
                table: "ApiIrCallLogs",
                columns: new[] { "AgencyId", "Service", "CalledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiIrCallLogs_BizId",
                table: "ApiIrCallLogs",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_ReminderLogs_AgencyId",
                table: "ReminderLogs",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderLogs_AgencyId_InstallmentId_OffsetDays_RecipientType",
                table: "ReminderLogs",
                columns: new[] { "AgencyId", "InstallmentId", "OffsetDays", "RecipientType" },
                unique: true,
                filter: "[InstallmentId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderLogs_AgencyId_RenewalWatchId_OffsetDays_RecipientType",
                table: "ReminderLogs",
                columns: new[] { "AgencyId", "RenewalWatchId", "OffsetDays", "RecipientType" },
                unique: true,
                filter: "[RenewalWatchId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderLogs_BizId",
                table: "ReminderLogs",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_ReminderLogs_InstallmentId",
                table: "ReminderLogs",
                column: "InstallmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderLogs_RenewalWatchId",
                table: "ReminderLogs",
                column: "RenewalWatchId");

            // Both tables carry AgencyId and were created after AddRowLevelSecurity's CREATE
            // SECURITY POLICY — join the same predicate function via ALTER (CLAUDE.md rule 10 is
            // not optional just because the table came later), same pattern as
            // AddImportColumnMapping's own migration.
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.ReminderLogs,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.ReminderLogs AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.ReminderLogs AFTER UPDATE,
                ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.ApiIrCallLogs,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.ApiIrCallLogs AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.ApiIrCallLogs AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                DROP FILTER PREDICATE ON dbo.ReminderLogs,
                DROP BLOCK PREDICATE ON dbo.ReminderLogs AFTER INSERT,
                DROP BLOCK PREDICATE ON dbo.ReminderLogs AFTER UPDATE,
                DROP FILTER PREDICATE ON dbo.ApiIrCallLogs,
                DROP BLOCK PREDICATE ON dbo.ApiIrCallLogs AFTER INSERT,
                DROP BLOCK PREDICATE ON dbo.ApiIrCallLogs AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "ApiIrCallLogs");

            migrationBuilder.DropTable(
                name: "ReminderLogs");
        }
    }
}
