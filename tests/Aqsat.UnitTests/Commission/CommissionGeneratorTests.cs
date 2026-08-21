using Aqsat.Application.Commission;

namespace Aqsat.UnitTests.Commission;

/// <summary>Task 13's own check (docs/TASKS.md): downShare + Σ instShare == totalCommission to the
/// rial; base is NetPremium, never TotalReceivable.</summary>
public class CommissionGeneratorTests
{
    [Fact]
    public void Slices_sum_exactly_to_total_commission_including_the_down_payment_share()
    {
        // NetPremium 10,700,000 * 5% = 535,000 total commission.
        // TotalReceivable = 10,700,000 (no service fee here) — down payment 1,700,000, 9 x 1,000,000.
        var installments = Enumerable.Range(1, 9).Select(i => (Guid.NewGuid(), 1_000_000m)).ToList();

        var slices = CommissionGenerator.Generate(
            netPremium: 10_700_000m, ratePercent: 5m, totalReceivable: 10_700_000m, downPayment: 1_700_000m,
            installments: installments);

        Assert.Equal(10, slices.Count);
        Assert.Equal(535_000m, slices.Sum(s => s.Amount));

        var downSlice = slices.Single(s => s.InstallmentId is null);
        Assert.True(downSlice.PayableImmediately);
        Assert.Equal(1_700_000m, downSlice.BasePortion);
    }

    [Fact]
    public void Base_is_net_premium_never_total_receivable()
    {
        // Service fee inflates TotalReceivable to 11,200,000 but must never enter the commission base.
        var installments = new List<(Guid, decimal)> { (Guid.NewGuid(), 11_200_000m) };

        var slices = CommissionGenerator.Generate(
            netPremium: 10_700_000m, ratePercent: 10m, totalReceivable: 11_200_000m, downPayment: 0m,
            installments: installments);

        // 10,700,000 * 10% = 1,070,000 — not 11,200,000 * 10% = 1,120,000.
        Assert.Equal(1_070_000m, slices.Sum(s => s.Amount));
    }

    [Fact]
    public void A_remainder_that_does_not_divide_evenly_lands_on_the_last_installment_slice()
    {
        // totalCommission = 1,000,000 * 3 / 100 ... pick numbers that don't divide evenly.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var installments = new List<(Guid, decimal)> { (a, 333_333m), (b, 333_333m), (c, 333_334m) };

        var slices = CommissionGenerator.Generate(
            netPremium: 1_000_000m, ratePercent: 7m, totalReceivable: 1_000_000m, downPayment: 0m,
            installments: installments);

        Assert.Equal(70_000m, slices.Sum(s => s.Amount));
        Assert.All(slices, s => Assert.False(s.PayableImmediately));
    }

    [Fact]
    public void No_down_payment_means_no_down_payment_slice()
    {
        var installments = new List<(Guid, decimal)> { (Guid.NewGuid(), 5_000_000m) };

        var slices = CommissionGenerator.Generate(
            netPremium: 5_000_000m, ratePercent: 4m, totalReceivable: 5_000_000m, downPayment: 0m,
            installments: installments);

        Assert.Single(slices);
        Assert.DoesNotContain(slices, s => s.InstallmentId is null);
    }
}
