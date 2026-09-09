using Aqsat.Domain;
using Aqsat.Domain.Enums;

namespace Aqsat.Infrastructure.Risk;

/// <summary>
/// The weighted 0–1000 credit score (docs Phase 2A §8/§9). Six sub-scores, each 0–1000, combined
/// with the agency-configured weights. A sub-score with no evidence yet (e.g. on-time ratio before
/// the first settled installment) drops out and its weight is redistributed across the factors
/// that do have evidence — absence of history is neither good nor bad. Pure function: no DB, no
/// clock; every date/money calculation is unit-testable (CLAUDE.md definition of done).
/// </summary>
public static class CreditScoreEngine
{
    /// <summary>The neutral baseline each sub-score deviation is measured against for the factor
    /// impact display (doc §10's signed impact).</summary>
    private const int NeutralSubScore = 700;

    public static (int Score, IReadOnlyList<RiskFactor> Factors) Calculate(RiskFeatures f, RiskSettings s)
    {
        var parts = new List<(string Code, string Title, string Detail, decimal Weight, int? SubScore)>
        {
            (
                "PAYMENT_HISTORY", "سابقهٔ پرداخت",
                f.SettledInstallmentCount == 0
                    ? "هنوز قسط تسویه‌شده‌ای برای سنجش سابقهٔ پرداخت وجود ندارد"
                    : $"{f.OnTimeSettledCount} از {f.SettledInstallmentCount} قسط تسویه‌شده سر موعد پرداخت شده است",
                s.PaymentHistoryWeight,
                f.OnTimeRatePercent is { } rate ? Round(rate * 10m) : null
            ),
            (
                "CURRENT_DEBT", "بدهی جاری",
                f.CurrentDebtToman == 0
                    ? (f.PolicyCount > 0 ? "بدهی باز ندارید و همهٔ اقساط تسویه شده‌اند" : "بدهی ثبت‌شده‌ای وجود ندارد")
                    : $"از بدهی جاری، {f.OverdueAmountToman:N0} تومان معوق است",
                s.CurrentDebtWeight,
                CurrentDebtSubScore(f)
            ),
            (
                "LATE_PAYMENT", "تأخیر در پرداخت",
                f.MaxDaysOverdue == 0
                    ? "هیچ قسط سررسیدگذشتهٔ باز وجود ندارد"
                    : $"بیشترین تأخیر فعلی {f.MaxDaysOverdue} روز است",
                s.LatePaymentWeight,
                Math.Max(0, 1000 - f.MaxDaysOverdue * 10)
            ),
            (
                "RETURNED_CHEQUES", "چک برگشتی",
                f.ReturnedChequeCount == 0
                    ? "چک برگشتی ثبت نشده است"
                    : $"{f.ReturnedChequeCount} چک برگشتی ثبت شده است",
                s.ReturnedChequesWeight,
                Math.Max(0, 1000 - f.ReturnedChequeCount * 250)
            ),
            (
                "CUSTOMER_TENURE", "سابقهٔ همکاری",
                f.PolicyCount == 0
                    ? "هنوز بیمه‌نامه‌ای صادر نشده است"
                    : $"{f.TenureMonths} ماه از نخستین صدور می‌گذرد",
                s.CustomerTenureWeight,
                f.PolicyCount == 0 ? null : Math.Min(1000, 250 + f.TenureMonths * 25)
            ),
            (
                "INSURANCE_BEHAVIOR", "رفتار بیمه‌ای",
                f.PolicyCount == 0
                    ? "سابقهٔ بیمه‌ای برای ارزیابی وجود ندارد"
                    : f.RenewalCount > 0
                        ? $"{f.RenewalCount} بیمه‌نامهٔ تمدیدی و {f.ActivePolicyCount} بیمه‌نامهٔ فعال"
                        : $"{f.ActivePolicyCount} بیمه‌نامهٔ فعال بدون سابقهٔ تمدید",
                s.InsuranceBehaviorWeight,
                InsuranceBehaviorSubScore(f)
            ),
        };

        var available = parts.Where(p => p.SubScore is not null).ToList();
        var totalWeight = available.Sum(p => p.Weight);
        if (totalWeight <= 0)
        {
            return (0, Array.Empty<RiskFactor>());
        }

        var score = Round(available.Sum(p => p.SubScore!.Value * p.Weight) / totalWeight);

        var factors = parts
            .Select(p => new RiskFactor(
                p.Code,
                p.Title,
                p.Detail,
                Impact: (int)Math.Round(p.Weight * ((p.SubScore ?? NeutralSubScore) - NeutralSubScore)),
                Severity: SeverityOf(p.SubScore is null ? 0 : p.SubScore.Value - NeutralSubScore)))
            .ToList();

        return (score, factors);
    }

    /// <summary>Pure-debt view: the share of the current debt that is already overdue. All debt and
    /// no overdue → 1000; all overdue → 0. A customer with policies but zero open debt has fully
    /// honored everything — 1000.</summary>
    private static int? CurrentDebtSubScore(RiskFeatures f)
    {
        if (f.CurrentDebtToman == 0)
        {
            return f.PolicyCount > 0 ? 1000 : null;
        }

        var overdueShare = f.OverdueAmountToman / f.CurrentDebtToman;
        return Math.Max(0, Round(1000 - 1000m * overdueShare));
    }

    private static int? InsuranceBehaviorSubScore(RiskFeatures f)
    {
        if (f.PolicyCount == 0)
        {
            return null;
        }

        if (f.RenewalCount > 0)
        {
            return Math.Min(1000, 700 + f.RenewalCount * 100);
        }

        return f.ActivePolicyCount > 0 ? 600 : 400;
    }

    /// <summary>Doc §8: which band a raw score lands in, before any rule override.</summary>
    public static RiskLevel LevelForScore(int score, RiskSettings s) => score switch
    {
        var v when v >= s.VeryLowMinScore => RiskLevel.VeryLow,
        var v when v >= s.LowMinScore => RiskLevel.Low,
        var v when v >= s.MediumMinScore => RiskLevel.Medium,
        var v when v >= s.HighMinScore => RiskLevel.High,
        _ => RiskLevel.Critical,
    };

    private static RiskFactorSeverity SeverityOf(int deviation)
    {
        if (deviation > 0)
        {
            return RiskFactorSeverity.Positive;
        }

        return -deviation switch
        {
            >= 80 => RiskFactorSeverity.High,
            >= 30 => RiskFactorSeverity.Medium,
            _ => RiskFactorSeverity.Info,
        };
    }

    private static int Round(decimal value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
