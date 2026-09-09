using System.Text.Json;
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
/// docs Phase 2A §20 — the customer-scoped half of the risk API: latest assessment, run/re-run an
/// assessment, history, and the manual credit-limit override. All reads run in the caller's RLS
/// scope; a customer outside the scope is a plain 404 (rule 17 — never a silent empty result).
/// </summary>
[ApiController]
[Route("api/customers/{customerId:guid}")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class CustomerRiskController(
    AppDbContext dbContext,
    Aqsat.Infrastructure.Risk.RiskAssessmentService riskService,
    ICurrentUserContext currentUser) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("risk")]
    public async Task<ActionResult<CustomerRiskDto>> GetRisk(Guid customerId, CancellationToken ct)
    {
        if (!await CustomerExistsAsync(customerId, ct))
        {
            return NotFound();
        }

        var latest = await riskService.GetLatestAsync(customerId, ct);
        return Ok(new CustomerRiskDto(
            latest is not null,
            InsufficientData: false,
            latest is null ? null : Map(latest)));
    }

    /// <summary>Run (or re-run) the assessment now — the «ارزیابی مجدد» button (doc §16).</summary>
    [HttpPost("risk/assess")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<ActionResult<CustomerRiskDto>> Assess(Guid customerId, CancellationToken ct)
    {
        if (!await CustomerExistsAsync(customerId, ct))
        {
            return NotFound();
        }

        var result = await riskService.AssessAsync(
            customerId, RiskAssessmentSource.Manual, currentUser.UserId, currentUser.DisplayName, ct);

        return Ok(new CustomerRiskDto(
            !result.InsufficientData,
            result.InsufficientData,
            result.InsufficientData ? null : Map(result.Assessment)));
    }

    [HttpGet("risk/history")]
    public async Task<ActionResult<IReadOnlyList<RiskHistoryItemDto>>> History(Guid customerId, CancellationToken ct)
    {
        if (!await CustomerExistsAsync(customerId, ct))
        {
            return NotFound();
        }

        var history = await riskService.GetHistoryAsync(customerId, ct);
        return Ok(history.Select(a => new RiskHistoryItemDto(
            a.Id,
            a.CalculatedAt,
            a.Score,
            RiskLabels.LevelFa(a.RiskLevel),
            RiskLabels.DecisionFa(a.Decision),
            a.CreditLimitToman,
            TopFactors(a),
            RiskLabels.SourceFa(a.Source))).ToList());
    }

    [HttpGet("credit-limit")]
    public async Task<ActionResult<CustomerCreditLimitDto>> GetCreditLimit(Guid customerId, CancellationToken ct)
    {
        if (!await CustomerExistsAsync(customerId, ct))
        {
            return NotFound();
        }

        var (recommended, manual, effective) = await riskService.GetCreditLimitAsync(customerId, ct);
        return Ok(new CustomerCreditLimitDto(recommended, manual, effective));
    }

    [HttpPut("credit-limit")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<IActionResult> SetCreditLimit(
        Guid customerId, SetCustomerCreditLimitRequest request, CancellationToken ct)
    {
        try
        {
            await riskService.SetCreditLimitAsync(
                customerId, request.LimitToman, request.Reason, currentUser.UserId, currentUser.DisplayName, ct);
        }
        catch (RiskAssessmentException ex)
        {
            return ValidationProblem(ex.Message);
        }

        return NoContent();
    }

    private async Task<bool> CustomerExistsAsync(Guid customerId, CancellationToken ct) =>
        await dbContext.Customers.AsNoTracking().AnyAsync(c => c.Id == customerId, ct);

    private static RiskAssessmentDto Map(RiskAssessment a) => new(
        a.Id,
        a.CustomerId,
        a.Score,
        RiskLabels.LevelFa(a.RiskLevel),
        a.RiskLevel.ToString(),
        RiskLabels.DecisionFa(a.Decision),
        a.Decision.ToString(),
        a.ProbabilityOfDefault,
        a.CurrentDebtToman,
        a.OverdueAmountToman,
        a.OverdueCount,
        a.MaxDaysOverdue,
        a.ReturnedChequeCount,
        a.OnTimeRatePercent,
        a.SettledInstallmentCount,
        a.TenureMonths,
        a.CreditExposureToman,
        a.CreditLimitToman,
        a.CreditLimitIsOverride,
        ReadFactors(a.FactorsJson).Select(MapFactor).ToList(),
        ReadRules(a.TriggeredRulesJson).Select(MapRule).ToList(),
        RiskLabels.SourceFa(a.Source),
        a.ModelVersion,
        a.CalculatedAt);

    private static List<RiskFactor> ReadFactors(string json) =>
        JsonSerializer.Deserialize<List<RiskFactor>>(json, JsonOptions) ?? [];

    private static List<TriggeredRule> ReadRules(string json) =>
        JsonSerializer.Deserialize<List<TriggeredRule>>(json, JsonOptions) ?? [];

    private static RiskFactorDto MapFactor(RiskFactor f) => new(
        f.Code, f.TitleFa, f.DetailFa, f.Impact, f.Severity.ToString());

    private static TriggeredRuleDto MapRule(TriggeredRule r) => new(
        r.Code, r.NameFa, r.DescriptionFa,
        r.ForcedLevel is { } level ? RiskLabels.LevelFa(level) : null,
        r.ForcedDecision is { } decision ? RiskLabels.DecisionFa(decision) : null,
        r.IsPositive);

    /// <summary>Doc §17 — the two most negative and one most positive factor titles per row.</summary>
    private static IReadOnlyList<string> TopFactors(RiskAssessment a) =>
        ReadFactors(a.FactorsJson)
            .OrderBy(f => f.Impact).Take(2).Select(f => f.TitleFa)
            .Concat(ReadFactors(a.FactorsJson).OrderByDescending(f => f.Impact).Take(1).Select(f => f.TitleFa))
            .Distinct()
            .ToList();

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
