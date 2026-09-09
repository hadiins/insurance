using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// Per-agency credit-risk configuration (owner decision 2026-09-03 — weights, bands, base limit
/// and rule thresholds are per-agency, never hard-coded constants): score weights (doc §9), risk
/// bands (doc §8), credit-limit multipliers (doc §14), rule thresholds (doc §12) and the issuance
/// gate mode. 1:1 extension of Organization, keyed directly on OrganizationId like OrgSettings.
/// </summary>
public class RiskSettings
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;

    // ---- Score weights (doc §9) — must always sum to exactly 1.00, enforced on save ----
    public decimal PaymentHistoryWeight { get; set; } = 0.30m;
    public decimal CurrentDebtWeight { get; set; } = 0.20m;
    public decimal LatePaymentWeight { get; set; } = 0.20m;
    public decimal ReturnedChequesWeight { get; set; } = 0.15m;
    public decimal CustomerTenureWeight { get; set; } = 0.10m;
    public decimal InsuranceBehaviorWeight { get; set; } = 0.05m;

    // ---- Risk bands (doc §8): score >= min → that level; below HighMin is CRITICAL ----
    public int VeryLowMinScore { get; set; } = 800;
    public int LowMinScore { get; set; } = 700;
    public int MediumMinScore { get; set; } = 550;
    public int HighMinScore { get; set; } = 400;

    // ---- Decision thresholds (doc §13) ----
    public int ApproveMinScore { get; set; } = 700;
    public int DeclineBelowScore { get; set; } = 400;

    // ---- Credit limit (doc §14) ----
    public decimal BaseCreditLimitToman { get; set; } = 100_000_000m;
    public decimal VeryLowMultiplier { get; set; } = 1.00m;
    public decimal LowMultiplier { get; set; } = 1.00m;
    public decimal MediumMultiplier { get; set; } = 0.70m;
    public decimal HighMultiplier { get; set; } = 0.35m;
    public decimal CriticalMultiplier { get; set; } = 0m;

    // ---- Rule thresholds (doc §12) ----

    /// <summary>Rule 1: bounced cheques >= this → HIGH.</summary>
    public int BouncedChequeHighThreshold { get; set; } = 2;

    /// <summary>Rule 2: an installment overdue by at least this many days is "severe" → HIGH.</summary>
    public int SevereOverdueDays { get; set; } = 90;

    /// <summary>Rule 3: max days overdue >= this → HIGH.</summary>
    public int MaxLateDaysHighThreshold { get; set; } = 60;

    /// <summary>Rule 4 reuses OrgSettings.DefaultWriteOffDays (the نکول write-off window) as
    /// "definite default history" → CRITICAL — one source of truth for the same business concept.</summary>

    /// <summary>Rule 6: on-time rate at or above this percent → positive factor. Stored 0–100.</summary>
    public decimal OnTimeRatePositivePercent { get; set; } = 90m;

    // ---- Early-warning thresholds (doc §19) ----
    public int ScoreDropWarningPoints { get; set; } = 50;
    public decimal DebtGrowthWarningPercent { get; set; } = 25m;
    public decimal CreditLimitUtilizationWarningPercent { get; set; } = 80m;

    // ---- Issuance gate (owner decision 2026-09-03): how the DECISION affects the wizard ----
    public IssuanceGateMode IssuanceGateMode { get; set; } = IssuanceGateMode.Informational;

    public byte[] RowVersion { get; set; } = default!;
}

public static class RiskSettingsDefaults
{
    /// <summary>Weight sum guard (doc §9): the six weights must compose exactly one whole score.</summary>
    public static decimal WeightSum(RiskSettings s) =>
        s.PaymentHistoryWeight + s.CurrentDebtWeight + s.LatePaymentWeight
        + s.ReturnedChequesWeight + s.CustomerTenureWeight + s.InsuranceBehaviorWeight;
}
