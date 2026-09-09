using System.Text.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Risk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aqsat.Api.Controllers;

/// <summary>
/// The agency-wide half of the risk API (docs Phase 2A §20): dashboard, high-risk list, warnings
/// feed, and the manual-review queue with its workflow. Reads need Policy.Read; every workflow
/// mutation needs Policy.Write and writes its audit row in the same transaction (rule 29).
/// </summary>
[ApiController]
[Route("api/risk")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class RiskController(
    RiskQueryService queryService,
    RiskReviewService reviewService,
    ICurrentUserContext currentUser) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("dashboard")]
    public async Task<ActionResult<RiskDashboardApiDto>> Dashboard(CancellationToken ct)
    {
        var d = await queryService.GetDashboardAsync(ct);
        return Ok(new RiskDashboardApiDto(
            d.TotalCustomers, d.AssessedCustomers,
            d.VeryLowCount, d.LowCount, d.MediumCount, d.HighCount, d.CriticalCount,
            d.CurrentDebtToman, d.TotalOverdueToman, d.OnTimeRatePercent, d.DefaultRatePercent,
            d.OpenReviewsCount, d.UnreadWarningsCount,
            Map(d.ScoreTrend), Map(d.OverdueTrend), Map(d.OnTimeTrend), Map(d.HighRiskTrend),
            d.WarningsByType.Select(w => new WarningTypeCountApiDto(w.Type, w.TypeFa, w.Count)).ToList()));
    }

    [HttpGet("high-risk")]
    public async Task<ActionResult<IReadOnlyList<HighRiskRiskCustomerApiDto>>> HighRisk(CancellationToken ct)
    {
        var rows = await queryService.GetHighRiskAsync(ct);
        return Ok(rows.Select(r => new HighRiskRiskCustomerApiDto(
            r.CustomerId, r.FullName, r.Mobile,
            r.OverdueInstallmentCount, r.MaxDaysOverdue, r.BouncedChequeCount, r.OverdueAmountToman,
            r.Score,
            r.RiskLevel is { } level ? RiskLabels.LevelFa(level) : null,
            r.RiskLevel?.ToString(),
            r.Decision is { } decision ? RiskLabels.DecisionFa(decision) : null,
            r.Decision?.ToString())).ToList());
    }

    [HttpGet("warnings")]
    public async Task<ActionResult<IReadOnlyList<RiskWarningApiDto>>> Warnings(
        [FromQuery] bool? unreadOnly, CancellationToken ct)
    {
        var warnings = await queryService.GetWarningsAsync(unreadOnly, ct);
        return Ok(warnings.Select(w => new RiskWarningApiDto(
            w.Id, w.CustomerId, w.CustomerName,
            w.Type.ToString(), RiskQueryService.WarningTypeFa(w.Type),
            w.Message, w.IsRead, w.CreatedAt)).ToList());
    }

    [HttpPost("warnings/{warningId:guid}/read")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<IActionResult> MarkWarningRead(Guid warningId, CancellationToken ct)
    {
        try
        {
            await reviewService.MarkWarningReadAsync(warningId, ct);
        }
        catch (RiskAssessmentException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = ex.Message });
        }

        return NoContent();
    }

    [HttpGet("manual-reviews")]
    public async Task<ActionResult<IReadOnlyList<ManualReviewApiDto>>> ManualReviews(
        [FromQuery] string? status, CancellationToken ct)
    {
        ManualReviewStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ManualReviewStatus>(status, ignoreCase: true, out var value))
            {
                return BadRequest(new ProblemDetails { Status = 400, Title = "وضعیت بررسی نامعتبر است." });
            }

            parsed = value;
        }

        var reviews = await queryService.GetManualReviewsAsync(parsed, ct);
        return Ok(reviews.Select(Map).ToList());
    }

    [HttpGet("manual-reviews/reviewers")]
    public async Task<ActionResult<IReadOnlyList<ReviewerApiDto>>> Reviewers(CancellationToken ct)
    {
        var reviewers = await reviewService.GetReviewersAsync(ct);
        return Ok(reviewers.Select(r => new ReviewerApiDto(r.Id, r.FullName)).ToList());
    }

    [HttpGet("manual-reviews/{reviewId:guid}")]
    public async Task<ActionResult<ManualReviewApiDto>> ManualReviewDetail(Guid reviewId, CancellationToken ct)
    {
        try
        {
            var (review, rules) = await reviewService.GetReviewDetailAsync(reviewId, ct);
            return Ok(Map(new ManualReviewListItemDto(
                review.Id, review.CustomerId, review.Customer.FullName, review.AssessmentId,
                review.Assessment.Score, review.Assessment.RiskLevel, review.RecommendedDecision,
                review.FinalDecision, review.Status,
                review.AssignedToUser?.FullName,
                review.Assessment.CurrentDebtToman, review.Assessment.OverdueAmountToman,
                review.Assessment.OverdueCount, review.Assessment.ReturnedChequeCount,
                null, review.Note, review.CreatedAt, review.ResolvedAt)) with
            {
                TriggeredRules = rules.Select(r => new TriggeredRuleDto(
                    r.Code, r.NameFa, r.DescriptionFa,
                    r.ForcedLevel is { } level ? RiskLabels.LevelFa(level) : null,
                    r.ForcedDecision is { } decision ? RiskLabels.DecisionFa(decision) : null,
                    r.IsPositive)).ToList(),
            });
        }
        catch (RiskAssessmentException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = ex.Message });
        }
    }

    [HttpPost("manual-reviews/{reviewId:guid}/assign")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<IActionResult> Assign(Guid reviewId, AssignReviewRequest request, CancellationToken ct)
    {
        try
        {
            await reviewService.AssignAsync(
                reviewId, request.AssignedToUserId, currentUser.UserId, currentUser.DisplayName, ct);
        }
        catch (RiskAssessmentException ex)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = ex.Message });
        }

        return NoContent();
    }

    /// <summary>Doc §20's decision endpoint, keyed on the review case the assessment opened.</summary>
    [HttpPost("manual-reviews/{reviewId:guid}/decision")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<IActionResult> Decide(Guid reviewId, DecideReviewRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<RiskDecision>(request.FinalDecision, ignoreCase: true, out var decision))
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = "تصمیم نهایی نامعتبر است." });
        }

        try
        {
            await reviewService.DecideAsync(
                reviewId, decision, request.Note, currentUser.UserId, currentUser.DisplayName, ct);
        }
        catch (RiskAssessmentException ex)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = ex.Message });
        }

        return NoContent();
    }

    [HttpPost("manual-reviews/{reviewId:guid}/request-info")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<IActionResult> RequestMoreInfo(Guid reviewId, RequestMoreInfoRequest request, CancellationToken ct)
    {
        try
        {
            await reviewService.RequestMoreInfoAsync(
                reviewId, request.Note, currentUser.UserId, currentUser.DisplayName, ct);
        }
        catch (RiskAssessmentException ex)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = ex.Message });
        }

        return NoContent();
    }

    private static IReadOnlyList<TrendPointApiDto> Map(IReadOnlyList<TrendPointDto> points) =>
        points.Select(p => new TrendPointApiDto(p.Date, p.Value)).ToList();

    private static ManualReviewApiDto Map(ManualReviewListItemDto r) => new(
        r.Id, r.CustomerId, r.CustomerName, r.AssessmentId,
        r.Score,
        RiskLabels.LevelFa(r.RiskLevel), r.RiskLevel.ToString(),
        RiskLabels.DecisionFa(r.RecommendedDecision), r.RecommendedDecision.ToString(),
        r.FinalDecision is { } final ? RiskLabels.DecisionFa(final) : null,
        r.FinalDecision?.ToString(),
        r.Status.ToString(), ManualReviewLabels.StatusFa(r.Status),
        r.AssignedToName,
        r.CurrentDebtToman, r.OverdueAmountToman, r.OverdueCount, r.ReturnedChequeCount,
        ReadRules(r.TriggeredRulesJson),
        r.Note, r.CreatedAt, r.ResolvedAt);

    private static IReadOnlyList<TriggeredRuleDto> ReadRules(string? json) =>
        json is null
            ? []
            : JsonSerializer.Deserialize<List<TriggeredRule>>(json, JsonOptions)?.Select(r => new TriggeredRuleDto(
                r.Code, r.NameFa, r.DescriptionFa,
                r.ForcedLevel is { } level ? RiskLabels.LevelFa(level) : null,
                r.ForcedDecision is { } decision ? RiskLabels.DecisionFa(decision) : null,
                r.IsPositive)).ToList() ?? [];
}
