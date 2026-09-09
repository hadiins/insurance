using Aqsat.Domain;
using Aqsat.Domain.Enums;

namespace Aqsat.Infrastructure.Risk;

/// <summary>
/// The final APPROVE / MANUAL_REVIEW / DECLINE call (docs Phase 2A §13). Score bands decide first,
/// but an explicit rule always overrides — a 760 score with default history is DECLINE. Pure
/// function, unit-tested per doc §29.
/// </summary>
public static class RiskDecisionEngine
{
    public static RiskDecision Decide(int score, RiskLevel finalLevel, IReadOnlyList<TriggeredRule> rules, RiskSettings s)
    {
        // The strictest forced decision wins: DECLINE beats MANUAL_REVIEW beats APPROVE.
        var forced = RiskDecision.Approve;
        foreach (var rule in rules)
        {
            if (rule.ForcedDecision is { } decision && decision > forced)
            {
                forced = decision;
            }
        }

        if (forced != RiskDecision.Approve)
        {
            return forced;
        }

        if (finalLevel == RiskLevel.Critical)
        {
            return RiskDecision.Decline;
        }

        if (score >= s.ApproveMinScore)
        {
            return RiskDecision.Approve;
        }

        return score < s.DeclineBelowScore ? RiskDecision.Decline : RiskDecision.ManualReview;
    }
}

/// <summary>Credit limit (doc §14): base × risk multiplier, with the agency's manual override for
/// this customer winning outright when one exists.</summary>
public static class CreditLimitEngine
{
    public static (decimal Limit, bool IsOverride) Compute(RiskLevel level, RiskSettings s, decimal? manualOverride) =>
        manualOverride is { } overrideValue
            ? (overrideValue, true)
            : (Round(s.BaseCreditLimitToman * Multiplier(level, s)), false);

    private static decimal Multiplier(RiskLevel level, RiskSettings s) => level switch
    {
        RiskLevel.VeryLow => s.VeryLowMultiplier,
        RiskLevel.Low => s.LowMultiplier,
        RiskLevel.Medium => s.MediumMultiplier,
        RiskLevel.High => s.HighMultiplier,
        _ => s.CriticalMultiplier,
    };

    private static decimal Round(decimal value) => Math.Round(value, MidpointRounding.AwayFromZero);
}
