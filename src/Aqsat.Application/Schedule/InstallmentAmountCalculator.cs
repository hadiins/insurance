namespace Aqsat.Application.Schedule;

/// <summary>docs/PHASE-1-SPEC.md §3.2: financed = TotalPremium - DownPayment; base = floor(financed
/// / N); the rounding remainder lands on the last installment, not spread across all of them.</summary>
public static class InstallmentAmountCalculator
{
    public static IReadOnlyList<decimal> CalculateAmounts(decimal totalPremium, decimal downPayment, int installmentCount)
    {
        if (installmentCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(installmentCount), "تعداد اقساط باید مثبت باشد.");
        }

        var financed = totalPremium - downPayment;
        var baseAmount = Math.Floor(financed / installmentCount);
        var lastAmount = financed - baseAmount * (installmentCount - 1);

        var amounts = new List<decimal>(installmentCount);
        for (var i = 0; i < installmentCount - 1; i++)
        {
            amounts.Add(baseAmount);
        }

        amounts.Add(lastAmount);
        return amounts;
    }
}
