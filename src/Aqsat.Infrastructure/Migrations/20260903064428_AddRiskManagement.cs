using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerCreditLimits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LimitToman = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SetByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SetAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCreditLimits", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_CustomerCreditLimits_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiskAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    RiskLevel = table.Column<byte>(type: "tinyint", nullable: false),
                    Decision = table.Column<byte>(type: "tinyint", nullable: false),
                    ProbabilityOfDefault = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CurrentDebtToman = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OverdueAmountToman = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OverdueCount = table.Column<int>(type: "int", nullable: false),
                    MaxDaysOverdue = table.Column<int>(type: "int", nullable: false),
                    ReturnedChequeCount = table.Column<int>(type: "int", nullable: false),
                    OnTimeRatePercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SettledInstallmentCount = table.Column<int>(type: "int", nullable: false),
                    TenureMonths = table.Column<int>(type: "int", nullable: false),
                    CreditExposureToman = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreditLimitToman = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreditLimitIsOverride = table.Column<bool>(type: "bit", nullable: false),
                    FactorsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TriggeredRulesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<byte>(type: "tinyint", nullable: false),
                    ModelVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ScoreSource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AssessmentVersion = table.Column<int>(type: "int", nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskAssessments", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_RiskAssessments_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiskSettings",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentHistoryWeight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrentDebtWeight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LatePaymentWeight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReturnedChequesWeight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CustomerTenureWeight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    InsuranceBehaviorWeight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VeryLowMinScore = table.Column<int>(type: "int", nullable: false),
                    LowMinScore = table.Column<int>(type: "int", nullable: false),
                    MediumMinScore = table.Column<int>(type: "int", nullable: false),
                    HighMinScore = table.Column<int>(type: "int", nullable: false),
                    ApproveMinScore = table.Column<int>(type: "int", nullable: false),
                    DeclineBelowScore = table.Column<int>(type: "int", nullable: false),
                    BaseCreditLimitToman = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VeryLowMultiplier = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LowMultiplier = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MediumMultiplier = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    HighMultiplier = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CriticalMultiplier = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BouncedChequeHighThreshold = table.Column<int>(type: "int", nullable: false),
                    SevereOverdueDays = table.Column<int>(type: "int", nullable: false),
                    MaxLateDaysHighThreshold = table.Column<int>(type: "int", nullable: false),
                    OnTimeRatePositivePercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ScoreDropWarningPoints = table.Column<int>(type: "int", nullable: false),
                    DebtGrowthWarningPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreditLimitUtilizationWarningPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IssuanceGateMode = table.Column<byte>(type: "tinyint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskSettings", x => x.OrganizationId);
                    table.ForeignKey(
                        name: "FK_RiskSettings_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManualReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    RecommendedDecision = table.Column<byte>(type: "tinyint", nullable: false),
                    FinalDecision = table.Column<byte>(type: "tinyint", nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualReviews", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_ManualReviews_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManualReviews_RiskAssessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "RiskAssessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManualReviews_Users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiskWarnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BizId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskWarnings", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_RiskWarnings_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiskWarnings_RiskAssessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "RiskAssessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditLimits_AgencyId",
                table: "CustomerCreditLimits",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditLimits_AgencyId_CustomerId",
                table: "CustomerCreditLimits",
                columns: new[] { "AgencyId", "CustomerId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditLimits_BizId",
                table: "CustomerCreditLimits",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditLimits_CustomerId",
                table: "CustomerCreditLimits",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ManualReviews_AgencyId",
                table: "ManualReviews",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ManualReviews_AgencyId_Status_CreatedAt",
                table: "ManualReviews",
                columns: new[] { "AgencyId", "Status", "CreatedAt" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_ManualReviews_AssessmentId",
                table: "ManualReviews",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ManualReviews_AssignedToUserId",
                table: "ManualReviews",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ManualReviews_BizId",
                table: "ManualReviews",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_ManualReviews_CustomerId",
                table: "ManualReviews",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAssessments_AgencyId",
                table: "RiskAssessments",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAssessments_AgencyId_CustomerId_CalculatedAt",
                table: "RiskAssessments",
                columns: new[] { "AgencyId", "CustomerId", "CalculatedAt" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_RiskAssessments_BizId",
                table: "RiskAssessments",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskAssessments_CustomerId",
                table: "RiskAssessments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskWarnings_AgencyId",
                table: "RiskWarnings",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskWarnings_AgencyId_IsRead_CreatedAt",
                table: "RiskWarnings",
                columns: new[] { "AgencyId", "IsRead", "CreatedAt" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_RiskWarnings_AssessmentId",
                table: "RiskWarnings",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskWarnings_BizId",
                table: "RiskWarnings",
                column: "BizId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskWarnings_CustomerId",
                table: "RiskWarnings",
                column: "CustomerId");

            // RLS for the four agency-scoped risk tables (CLAUDE.md rule 10) — same predicate
            // family as migration AddRowLevelSecurity. RiskSettings is OrganizationId-keyed like
            // OrgSettings and deliberately stays outside the security policy.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.RiskAssessments,
                    ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.RiskWarnings,
                    ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.ManualReviews,
                    ADD FILTER PREDICATE dbo.fn_AgencyAccessPredicate(AgencyId) ON dbo.CustomerCreditLimits;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY dbo.AgencyAccessPolicy
                    DROP FILTER PREDICATE ON dbo.RiskAssessments,
                    DROP FILTER PREDICATE ON dbo.RiskWarnings,
                    DROP FILTER PREDICATE ON dbo.ManualReviews,
                    DROP FILTER PREDICATE ON dbo.CustomerCreditLimits;
                """);

            migrationBuilder.DropTable(
                name: "CustomerCreditLimits");

            migrationBuilder.DropTable(
                name: "ManualReviews");

            migrationBuilder.DropTable(
                name: "RiskSettings");

            migrationBuilder.DropTable(
                name: "RiskWarnings");

            migrationBuilder.DropTable(
                name: "RiskAssessments");
        }
    }
}
