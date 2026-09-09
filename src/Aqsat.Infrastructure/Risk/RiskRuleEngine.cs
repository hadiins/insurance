using Aqsat.Domain;
using Aqsat.Domain.Enums;

namespace Aqsat.Infrastructure.Risk;

/// <summary>
/// The six fixed built-in rules of docs Phase 2A §12, thresholds per agency (owner decision
/// 2026-09-03: no free-form rule builder in 2A — full rule CRUD is 2B). Rules force a risk level
/// and/or a decision that overrides the score band (doc §13: a critical rule beats a high score).
/// Pure function. Doc Rule 5's "REVIEW" is a DECISION (MANUAL_REVIEW), not a risk level.
/// </summary>
public static class RiskRuleEngine
{
    public static IReadOnlyList<TriggeredRule> Evaluate(RiskFeatures f, RiskSettings s, decimal effectiveCreditLimit)
    {
        var rules = new List<TriggeredRule>();

        if (f.ReturnedChequeCount >= s.BouncedChequeHighThreshold)
        {
            rules.Add(new TriggeredRule(
                "RETURNED_CHEQUES_HIGH", "چک برگشتی متعدد",
                $"{f.ReturnedChequeCount} چک برگشتی ثبت شده است",
                RiskLevel.High, null, IsPositive: false));
        }

        if (f.MaxDaysOverdue >= s.SevereOverdueDays)
        {
            rules.Add(new TriggeredRule(
                "SEVERE_OVERDUE", "معوقهٔ شدید",
                $"قسطی {f.MaxDaysOverdue} روز از سررسید گذشته است",
                RiskLevel.High, null, IsPositive: false));
        }

        if (f.MaxDaysOverdue >= s.MaxLateDaysHighThreshold)
        {
            rules.Add(new TriggeredRule(
                "MAX_LATE_DAYS", "تأخیر طولانی",
                $"بیشترین تأخیر {f.MaxDaysOverdue} روز است",
                RiskLevel.High, null, IsPositive: false));
        }

        if (f.HasDefaultHistory)
        {
            rules.Add(new TriggeredRule(
                "DEFAULT_HISTORY", "سابقهٔ نکول",
                "قسط معوقی فراتر از پنجرهٔ سوخت نکول وجود دارد",
                RiskLevel.Critical, RiskDecision.Decline, IsPositive: false));
        }

        if (effectiveCreditLimit > 0 && f.CurrentDebtToman > effectiveCreditLimit)
        {
            rules.Add(new TriggeredRule(
                "OVER_CREDIT_LIMIT", "عبور از سقف اعتبار",
                $"بدهی جاری از سقف اعتبار عبور کرده است",
                null, RiskDecision.ManualReview, IsPositive: false));
        }

        if (f.OnTimeRatePercent is { } rate && rate >= s.OnTimeRatePositivePercent)
        {
            rules.Add(new TriggeredRule(
                "ON_TIME_REGULAR", "پرداخت منظم",
                $"{rate:N0} درصد اقساط تسویه‌شده سر موعد پرداخت شده است",
                null, null, IsPositive: true));
        }

        return rules;
    }

    /// <summary>The strictest level any fired rule forces; null when none did. Never lowers the
    /// score band — rules only escalate (doc §13).</summary>
    public static RiskLevel? ForcedLevel(IReadOnlyList<TriggeredRule> rules)
    {
        RiskLevel? max = null;
        foreach (var rule in rules)
        {
            if (rule.ForcedLevel is { } level && (max is null || level > max))
            {
                max = level;
            }
        }

        return max;
    }
}
