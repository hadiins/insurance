using Aqsat.Domain.Enums;

namespace Aqsat.Api.Contracts;

public sealed record RiskFactorDto(string Code, string Title, string Detail, int Impact, string Severity);

public sealed record TriggeredRuleDto(
    string Code, string Name, string Description, string? ForcedLevel, string? ForcedDecision, bool IsPositive);

public sealed record RiskAssessmentDto(
    Guid Id,
    Guid CustomerId,
    int Score,
    string RiskLevel,
    string RiskLevelKey,
    string Decision,
    string DecisionKey,
    decimal? ProbabilityOfDefault,
    decimal CurrentDebtToman,
    decimal OverdueAmountToman,
    int OverdueCount,
    int MaxDaysOverdue,
    int ReturnedChequeCount,
    decimal OnTimeRatePercent,
    int SettledInstallmentCount,
    int TenureMonths,
    decimal CreditExposureToman,
    decimal CreditLimitToman,
    bool CreditLimitIsOverride,
    IReadOnlyList<RiskFactorDto> Factors,
    IReadOnlyList<TriggeredRuleDto> TriggeredRules,
    string Source,
    string ModelVersion,
    DateTimeOffset CalculatedAtUtc);

/// <summary>GET /api/customers/{id}/risk — the customer-file risk tab's whole payload. The
/// empty-result state is explicit (doc §23), never a silent null.</summary>
public sealed record CustomerRiskDto(
    bool HasAssessment,
    bool InsufficientData,
    RiskAssessmentDto? Latest);

public sealed record RiskHistoryItemDto(
    Guid Id,
    DateTimeOffset CalculatedAtUtc,
    int Score,
    string RiskLevel,
    string Decision,
    decimal CreditLimitToman,
    IReadOnlyList<string> TopFactors,
    string Source);

public sealed record CustomerCreditLimitDto(
    decimal? RecommendedToman,
    decimal? OverrideToman,
    decimal EffectiveToman);

public sealed record SetCustomerCreditLimitRequest(decimal LimitToman, string? Reason);

/// <summary>Per-agency risk configuration (doc §8/§9/§12/§14 + owner decision 2026-09-03). Weights
/// must sum to exactly 1.00 and bands must stay ordered — validated on save.</summary>
public sealed record RiskSettingsDto(
    decimal PaymentHistoryWeight,
    decimal CurrentDebtWeight,
    decimal LatePaymentWeight,
    decimal ReturnedChequesWeight,
    decimal CustomerTenureWeight,
    decimal InsuranceBehaviorWeight,
    int VeryLowMinScore,
    int LowMinScore,
    int MediumMinScore,
    int HighMinScore,
    int ApproveMinScore,
    int DeclineBelowScore,
    decimal BaseCreditLimitToman,
    decimal VeryLowMultiplier,
    decimal LowMultiplier,
    decimal MediumMultiplier,
    decimal HighMultiplier,
    decimal CriticalMultiplier,
    int BouncedChequeHighThreshold,
    int SevereOverdueDays,
    int MaxLateDaysHighThreshold,
    decimal OnTimeRatePositivePercent,
    int ScoreDropWarningPoints,
    decimal DebtGrowthWarningPercent,
    decimal CreditLimitUtilizationWarningPercent,
    string IssuanceGateMode);

public sealed record UpdateRiskSettingsRequest(
    decimal? PaymentHistoryWeight,
    decimal? CurrentDebtWeight,
    decimal? LatePaymentWeight,
    decimal? ReturnedChequesWeight,
    decimal? CustomerTenureWeight,
    decimal? InsuranceBehaviorWeight,
    int? VeryLowMinScore,
    int? LowMinScore,
    int? MediumMinScore,
    int? HighMinScore,
    int? ApproveMinScore,
    int? DeclineBelowScore,
    decimal? BaseCreditLimitToman,
    decimal? VeryLowMultiplier,
    decimal? LowMultiplier,
    decimal? MediumMultiplier,
    decimal? HighMultiplier,
    decimal? CriticalMultiplier,
    int? BouncedChequeHighThreshold,
    int? SevereOverdueDays,
    int? MaxLateDaysHighThreshold,
    decimal? OnTimeRatePositivePercent,
    int? ScoreDropWarningPoints,
    decimal? DebtGrowthWarningPercent,
    decimal? CreditLimitUtilizationWarningPercent,
    string? IssuanceGateMode);

public static class RiskLabels
{
    public static string LevelFa(RiskLevel level) => level switch
    {
        RiskLevel.VeryLow => "خیلی پایین",
        RiskLevel.Low => "پایین",
        RiskLevel.Medium => "متوسط",
        RiskLevel.High => "بالا",
        _ => "بحرانی",
    };

    public static string DecisionFa(RiskDecision decision) => decision switch
    {
        RiskDecision.Approve => "تأیید",
        RiskDecision.ManualReview => "بررسی دستی",
        _ => "رد",
    };

    public static string SourceFa(RiskAssessmentSource source) => source switch
    {
        RiskAssessmentSource.Manual => "دستی",
        _ => "خودکار (شبانه)",
    };
}
