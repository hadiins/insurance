using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Risk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// Per-agency risk configuration (owner decision 2026-09-03): weights, score bands, credit-limit
/// base/multipliers, rule thresholds, early-warning thresholds and the issuance gate mode. GET
/// returns the live defaults when the agency never saved a row, so the settings page always shows
/// something editable (doc §23 — never a blank page).
/// </summary>
[ApiController]
[Route("api/risk/settings")]
[Authorize(Policy = Permissions.SettingsWrite)]
public sealed class RiskSettingsController(
    AppDbContext dbContext,
    Aqsat.Infrastructure.Risk.RiskAssessmentService riskService,
    ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RiskSettingsDto>> Get(CancellationToken ct)
    {
        var settings = await riskService.GetSettingsAsync(currentUser.ActiveOrganizationId, ct);
        return Ok(Map(settings));
    }

    [HttpPut]
    public async Task<ActionResult<RiskSettingsDto>> Update(UpdateRiskSettingsRequest request, CancellationToken ct)
    {
        var organizationId = currentUser.ActiveOrganizationId;
        var settings = await dbContext.RiskSettings
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct);

        var isNew = settings is null;
        settings ??= new RiskSettings { OrganizationId = organizationId };

        if (request.PaymentHistoryWeight is { } v1) settings.PaymentHistoryWeight = v1;
        if (request.CurrentDebtWeight is { } v2) settings.CurrentDebtWeight = v2;
        if (request.LatePaymentWeight is { } v3) settings.LatePaymentWeight = v3;
        if (request.ReturnedChequesWeight is { } v4) settings.ReturnedChequesWeight = v4;
        if (request.CustomerTenureWeight is { } v5) settings.CustomerTenureWeight = v5;
        if (request.InsuranceBehaviorWeight is { } v6) settings.InsuranceBehaviorWeight = v6;
        if (request.VeryLowMinScore is { } v7) settings.VeryLowMinScore = v7;
        if (request.LowMinScore is { } v8) settings.LowMinScore = v8;
        if (request.MediumMinScore is { } v9) settings.MediumMinScore = v9;
        if (request.HighMinScore is { } v10) settings.HighMinScore = v10;
        if (request.ApproveMinScore is { } v11) settings.ApproveMinScore = v11;
        if (request.DeclineBelowScore is { } v12) settings.DeclineBelowScore = v12;
        if (request.BaseCreditLimitToman is { } v13) settings.BaseCreditLimitToman = v13;
        if (request.VeryLowMultiplier is { } v14) settings.VeryLowMultiplier = v14;
        if (request.LowMultiplier is { } v15) settings.LowMultiplier = v15;
        if (request.MediumMultiplier is { } v16) settings.MediumMultiplier = v16;
        if (request.HighMultiplier is { } v17) settings.HighMultiplier = v17;
        if (request.CriticalMultiplier is { } v18) settings.CriticalMultiplier = v18;
        if (request.BouncedChequeHighThreshold is { } v19) settings.BouncedChequeHighThreshold = v19;
        if (request.SevereOverdueDays is { } v20) settings.SevereOverdueDays = v20;
        if (request.MaxLateDaysHighThreshold is { } v21) settings.MaxLateDaysHighThreshold = v21;
        if (request.OnTimeRatePositivePercent is { } v22) settings.OnTimeRatePositivePercent = v22;
        if (request.ScoreDropWarningPoints is { } v23) settings.ScoreDropWarningPoints = v23;
        if (request.DebtGrowthWarningPercent is { } v24) settings.DebtGrowthWarningPercent = v24;
        if (request.CreditLimitUtilizationWarningPercent is { } v25) settings.CreditLimitUtilizationWarningPercent = v25;
        if (request.IssuanceGateMode is { } gateMode && Enum.TryParse<IssuanceGateMode>(gateMode, ignoreCase: true, out var parsedGate))
        {
            settings.IssuanceGateMode = parsedGate;
        }

        // Sum == 1 alone admits nonsense like two negative weights offset by >1 ones; a negative
        // weight flips its factor's contribution to the score. Each weight is a share of one.
        if (settings.PaymentHistoryWeight is < 0 or > 1 || settings.CurrentDebtWeight is < 0 or > 1
            || settings.LatePaymentWeight is < 0 or > 1 || settings.ReturnedChequesWeight is < 0 or > 1
            || settings.CustomerTenureWeight is < 0 or > 1 || settings.InsuranceBehaviorWeight is < 0 or > 1)
        {
            return ValidationProblem("هر وزن عامل باید بین ۰ و ۱ باشد.");
        }

        if (Math.Abs(RiskSettingsDefaults.WeightSum(settings) - 1m) > 0.0001m)
        {
            return ValidationProblem("مجموع وزن عوامل باید دقیقاً ۱ باشد.");
        }

        if (settings.HighMinScore <= 0 || settings.MediumMinScore < settings.HighMinScore
            || settings.LowMinScore < settings.MediumMinScore || settings.VeryLowMinScore < settings.LowMinScore
            || settings.VeryLowMinScore > 1000)
        {
            return ValidationProblem("بازه‌های امتیاز باید صعودی و بین ۰ تا ۱۰۰۰ باشند.");
        }

        if (settings.DeclineBelowScore >= settings.ApproveMinScore)
        {
            return ValidationProblem("حد رد باید کمتر از حد تأیید باشد.");
        }

        if (settings.BaseCreditLimitToman < 0
            || settings.VeryLowMultiplier is < 0 or > 1 || settings.LowMultiplier is < 0 or > 1
            || settings.MediumMultiplier is < 0 or > 1 || settings.HighMultiplier is < 0 or > 1
            || settings.CriticalMultiplier is < 0 or > 1)
        {
            return ValidationProblem("ضرایب سقف اعتبار باید بین ۰ و ۱ و سقف پایه مثبت باشند.");
        }

        if (settings.BouncedChequeHighThreshold <= 0 || settings.SevereOverdueDays <= 0
            || settings.MaxLateDaysHighThreshold <= 0 || settings.OnTimeRatePositivePercent is <= 0 or > 100
            || settings.ScoreDropWarningPoints <= 0 || settings.DebtGrowthWarningPercent <= 0
            || settings.CreditLimitUtilizationWarningPercent is <= 0 or > 100)
        {
            return ValidationProblem("آستانه‌های قوانین و هشدار باید مقادیر مثبت معتبری باشند.");
        }

        if (isNew)
        {
            dbContext.RiskSettings.Add(settings);
        }

        // Same transaction as the change itself (rule 29): the audit row joins the tracked graph
        // before the single SaveChanges.
        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = organizationId,
            UserId = currentUser.UserId,
            UserDisplayName = currentUser.DisplayName,
            EntityType = nameof(RiskSettings),
            EntityId = organizationId,
            PolicyId = Guid.Empty,
            Action = AuditAction.RiskAssessed,
            Description = isNew
                ? "تنظیمات اعتبار و ریسک نمایندگی برای نخستین‌بار ذخیره شد"
                : "تنظیمات اعتبار و ریسک نمایندگی ویرایش شد",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync(ct);

        return Ok(Map(settings));
    }

    private static RiskSettingsDto Map(RiskSettings s) => new(
        s.PaymentHistoryWeight,
        s.CurrentDebtWeight,
        s.LatePaymentWeight,
        s.ReturnedChequesWeight,
        s.CustomerTenureWeight,
        s.InsuranceBehaviorWeight,
        s.VeryLowMinScore,
        s.LowMinScore,
        s.MediumMinScore,
        s.HighMinScore,
        s.ApproveMinScore,
        s.DeclineBelowScore,
        s.BaseCreditLimitToman,
        s.VeryLowMultiplier,
        s.LowMultiplier,
        s.MediumMultiplier,
        s.HighMultiplier,
        s.CriticalMultiplier,
        s.BouncedChequeHighThreshold,
        s.SevereOverdueDays,
        s.MaxLateDaysHighThreshold,
        s.OnTimeRatePositivePercent,
        s.ScoreDropWarningPoints,
        s.DebtGrowthWarningPercent,
        s.CreditLimitUtilizationWarningPercent,
        s.IssuanceGateMode.ToString());

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
