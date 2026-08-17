namespace Aqsat.Application.Schedule;

/// <summary>docs/PHASE-1-SPEC.md §3.2: "pick the down payment that makes base land on a round
/// number" — a hint only, the agent can always override (never enforced).</summary>
public static class DownPaymentSuggester
{
    public static decimal Suggest(decimal totalPremium, int installmentCount, decimal roundToToman = 10_000m)
    {
        if (installmentCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(installmentCount), "تعداد اقساط باید مثبت باشد.");
        }

        var estimatedBase = totalPremium / installmentCount;
        var roundedBase = Math.Floor(estimatedBase / roundToToman) * roundToToman;
        if (roundedBase <= 0)
        {
            roundedBase = roundToToman;
        }

        var financed = roundedBase * installmentCount;
        var suggestedDownPayment = totalPremium - financed;
        return suggestedDownPayment < 0 ? 0 : suggestedDownPayment;
    }
}
