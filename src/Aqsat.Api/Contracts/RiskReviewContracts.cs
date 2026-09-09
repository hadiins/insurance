using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Risk;

namespace Aqsat.Api.Contracts;

/// <summary>GET /api/risk/dashboard — the KPI tiles and the six chart series (docs Phase 2A §6).
/// Dates are UTC day buckets; the frontend composes the Jalali label.</summary>
public sealed record RiskDashboardApiDto(
    int TotalCustomers,
    int AssessedCustomers,
    int VeryLowCount,
    int LowCount,
    int MediumCount,
    int HighCount,
    int CriticalCount,
    decimal CurrentDebtToman,
    decimal TotalOverdueToman,
    decimal? OnTimeRatePercent,
    decimal DefaultRatePercent,
    int OpenReviewsCount,
    int UnreadWarningsCount,
    IReadOnlyList<TrendPointApiDto> ScoreTrend,
    IReadOnlyList<TrendPointApiDto> OverdueTrend,
    IReadOnlyList<TrendPointApiDto> OnTimeTrend,
    IReadOnlyList<TrendPointApiDto> HighRiskTrend,
    IReadOnlyList<WarningTypeCountApiDto> WarningsByType);

public sealed record TrendPointApiDto(DateTimeOffset Date, decimal Value);

public sealed record WarningTypeCountApiDto(string Type, string TypeFa, int Count);

/// <summary>GET /api/risk/high-risk — the operational list (overdue/bounced) merged with the
/// customer's latest assessment; Score/RiskLevel/Decision are null before the first assessment.</summary>
public sealed record HighRiskRiskCustomerApiDto(
    Guid CustomerId,
    string FullName,
    string? Mobile,
    int OverdueInstallmentCount,
    int MaxDaysOverdue,
    int BouncedChequeCount,
    decimal OverdueAmountToman,
    int? Score,
    string? RiskLevel,
    string? RiskLevelKey,
    string? Decision,
    string? DecisionKey);

public sealed record RiskWarningApiDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string Type,
    string TypeFa,
    string Message,
    bool IsRead,
    DateTimeOffset CreatedAt);

/// <summary>GET /api/risk/manual-reviews — one row of the review queue (docs Phase 2A §15).</summary>
public sealed record ManualReviewApiDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    Guid AssessmentId,
    int Score,
    string RiskLevel,
    string RiskLevelKey,
    string RecommendedDecision,
    string RecommendedDecisionKey,
    string? FinalDecision,
    string? FinalDecisionKey,
    string Status,
    string StatusFa,
    string? AssignedToName,
    decimal CurrentDebtToman,
    decimal OverdueAmountToman,
    int OverdueCount,
    int ReturnedChequeCount,
    IReadOnlyList<TriggeredRuleDto> TriggeredRules,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

public sealed record ReviewerApiDto(Guid Id, string FullName);

public sealed record AssignReviewRequest(Guid? AssignedToUserId);

public sealed record DecideReviewRequest(string FinalDecision, string? Note);

public sealed record RequestMoreInfoRequest(string? Note);

public static class ManualReviewLabels
{
    public static string StatusFa(ManualReviewStatus status) => status switch
    {
        ManualReviewStatus.Pending => "در انتظار بررسی",
        ManualReviewStatus.InReview => "در حال بررسی",
        ManualReviewStatus.Approved => "تأییدشده",
        ManualReviewStatus.Rejected => "ردشده",
        _ => "نیاز به اطلاعات بیشتر",
    };
}
