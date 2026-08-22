using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformUpdateSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UpdatePackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Version = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReleaseNotesFa = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ImageTag = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(71)", maxLength: 71, nullable: false),
                    SignatureBase64 = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    MinimumFromVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    HasDbMigration = table.Column<bool>(type: "bit", nullable: false),
                    IsSecurityUpdate = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsYanked = table.Column<bool>(type: "bit", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpdatePackages", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "UpdateRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ToVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StartedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CurrentStage = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ProgressPercent = table.Column<int>(type: "int", nullable: false),
                    BackupPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BackupSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ErrorDetail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdaterRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpdateRuns", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_UpdateRuns_UpdatePackages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "UpdatePackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UpdateStageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StageNo = table.Column<int>(type: "int", nullable: false),
                    StageName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    Output = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpdateStageLogs", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_UpdateStageLogs_UpdateRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "UpdateRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UpdatePackages_BizId",
                table: "UpdatePackages",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_UpdatePackages_Version",
                table: "UpdatePackages",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UpdateRuns_BizId",
                table: "UpdateRuns",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_UpdateRuns_PackageId",
                table: "UpdateRuns",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_UpdateRuns_StartedAt",
                table: "UpdateRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_UpdateRuns_Status",
                table: "UpdateRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_UpdateStageLogs_BizId",
                table: "UpdateStageLogs",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_UpdateStageLogs_RunId_StageNo",
                table: "UpdateStageLogs",
                columns: new[] { "RunId", "StageNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UpdateStageLogs");

            migrationBuilder.DropTable(
                name: "UpdateRuns");

            migrationBuilder.DropTable(
                name: "UpdatePackages");
        }
    }
}
