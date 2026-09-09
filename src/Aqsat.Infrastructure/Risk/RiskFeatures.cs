namespace Aqsat.Infrastructure.Risk;

/// <summary>
/// The customer's financial position at one moment in time — everything the score and rule
/// engines consume (docs Phase 2A §9/§12). Derived from installments/payments/cheques/collateral,
/// never copied into a separate model. Null sub-features are "no evidence yet" and drop out of the
/// weighted score instead of counting as good or bad.
/// </summary>
public sealed record RiskFeatures(
    int PolicyCount,
    int ActivePolicyCount,
    int RenewalCount,
    int TenureMonths,
    decimal CurrentDebtToman,
    decimal OverdueAmountToman,
    int OverdueCount,
    int MaxDaysOverdue,
    bool HasDefaultHistory,
    int ReturnedChequeCount,
    int SettledInstallmentCount,
    int OnTimeSettledCount)
{
    /// <summary>Percent of settled installments fully paid by their due date; null before the first
    /// settlement — an on-time ratio over zero samples says nothing.</summary>
    public decimal? OnTimeRatePercent =>
        SettledInstallmentCount == 0 ? null : 100m * OnTimeSettledCount / SettledInstallmentCount;

    /// <summary>A brand-new customer with no policies and no installments has nothing to score —
    /// the assessment endpoint reports the insufficient-data state instead of inventing a score.</summary>
    public bool HasAnyEvidence => PolicyCount > 0 || SettledInstallmentCount > 0 || OverdueCount > 0;
}
