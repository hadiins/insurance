using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// One credit-risk evaluation of a customer (docs Phase 2A §17/§18): the weighted 0–1000 score,
/// its risk level, the final decision, the explainability factors/rules, AND a full snapshot of
/// the customer's financial position at assessment time — later changes to the underlying
/// installments/cheques never rewrite history. The future ML layer (Phase 2B) plugs in beside the
/// rule engine, never replacing it; ModelVersion/ScoreSource/AssessmentVersion are reserved for
/// that from day one. Amounts in TOMAN (rule 19). Never free text about the person (rule 8).
/// </summary>
public class RiskAssessment : AgencyOwnedEntity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;

    /// <summary>0–1000, weighted across the factor set configured per agency (RiskSettings).</summary>
    public int Score { get; set; }

    public RiskLevel RiskLevel { get; set; }

    public RiskDecision Decision { get; set; }

    /// <summary>Reserved for Phase 2B ML — always null in 2A (doc §21).</summary>
    public decimal? ProbabilityOfDefault { get; set; }

    // ---- Snapshot (doc §18) — figures of the moment, frozen ----

    /// <summary>Open installment balance across all of the customer's policies.</summary>
    public decimal CurrentDebtToman { get; set; }

    public decimal OverdueAmountToman { get; set; }
    public int OverdueCount { get; set; }

    /// <summary>Days past due of the oldest currently-overdue installment; 0 when none.</summary>
    public int MaxDaysOverdue { get; set; }

    public int ReturnedChequeCount { get; set; }

    /// <summary>Settled installments paid by their due date ÷ all settled installments, percent.</summary>
    public decimal OnTimeRatePercent { get; set; }

    public int SettledInstallmentCount { get; set; }

    /// <summary>Months since the customer's first policy issue date; 0 for a brand-new customer.</summary>
    public int TenureMonths { get; set; }

    /// <summary>Same figure as CurrentDebtToman, named as the doc's credit-exposure concept.</summary>
    public decimal CreditExposureToman { get; set; }

    public decimal CreditLimitToman { get; set; }

    /// <summary>True when the limit is the agency's manual override, not the computed recommendation.</summary>
    public bool CreditLimitIsOverride { get; set; }

    // ---- Explainability (doc §26): no score is ever shown without its factors ----

    /// <summary>Serialized list of {code, title, detail, impact, severity} — structured, composed
    /// at write time.</summary>
    public string FactorsJson { get; set; } = "[]";

    /// <summary>Serialized list of the rules that fired, with their forced level/decision when any.</summary>
    public string TriggeredRulesJson { get; set; } = "[]";

    public RiskAssessmentSource Source { get; set; }

    public string ModelVersion { get; set; } = "rules-2a";

    public string ScoreSource { get; set; } = "rule-engine";

    public int AssessmentVersion { get; set; } = 1;

    public DateTimeOffset CalculatedAt { get; set; }
}
