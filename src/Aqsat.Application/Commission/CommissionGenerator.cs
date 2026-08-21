namespace Aqsat.Application.Commission;

public readonly record struct CommissionSlice(Guid? InstallmentId, decimal BasePortion, decimal Amount, bool PayableImmediately);

/// <summary>
/// docs/PHASE-1-SPEC.md §2.8/§3.5 — one slice per installment plus one down-payment slice. Base is
/// NetPremium only, never TotalReceivable (the service fee is the agency's, not the marketer's).
/// Slices are proportional to each installment's *amount*, not count. Guard:
/// downShare + Σ instShare == totalCommission to the rial — the rounding remainder lands on the
/// last installment slice, same pattern as InstallmentAmountCalculator.
/// </summary>
public static class CommissionGenerator
{
    public static IReadOnlyList<CommissionSlice> Generate(
        decimal netPremium,
        decimal ratePercent,
        decimal totalReceivable,
        decimal downPayment,
        IReadOnlyList<(Guid InstallmentId, decimal Amount)> installments)
    {
        if (installments.Count == 0)
        {
            throw new ArgumentException("حداقل یک قسط برای تولید کارمزد لازم است.", nameof(installments));
        }

        var totalCommission = Math.Round(netPremium * ratePercent / 100m, 0, MidpointRounding.ToEven);
        var slices = new List<CommissionSlice>();
        var allocated = 0m;

        if (downPayment > 0)
        {
            var downShare = Math.Round(totalCommission * (downPayment / totalReceivable), 0, MidpointRounding.ToEven);
            slices.Add(new CommissionSlice(null, downPayment, downShare, PayableImmediately: true));
            allocated += downShare;
        }

        for (var i = 0; i < installments.Count; i++)
        {
            var (installmentId, amount) = installments[i];
            var isLast = i == installments.Count - 1;
            var share = isLast
                ? totalCommission - allocated
                : Math.Round(totalCommission * (amount / totalReceivable), 0, MidpointRounding.ToEven);

            slices.Add(new CommissionSlice(installmentId, amount, share, PayableImmediately: false));
            allocated += share;
        }

        return slices;
    }
}
