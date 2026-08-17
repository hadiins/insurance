using Aqsat.Application.Schedule;

namespace Aqsat.UnitTests.Schedule;

/// <summary>Task 8's own check (docs/TASKS.md): "A 10,700,000 premium with a 1,700,000 down
/// payment and 9 installments yields exactly 1,000,000 each."</summary>
public class InstallmentAmountCalculatorTests
{
    [Fact]
    public void The_specs_own_worked_example_yields_nine_equal_installments()
    {
        var amounts = InstallmentAmountCalculator.CalculateAmounts(
            totalPremium: 10_700_000m, downPayment: 1_700_000m, installmentCount: 9);

        Assert.Equal(9, amounts.Count);
        Assert.All(amounts, a => Assert.Equal(1_000_000m, a));
    }

    [Fact]
    public void A_remainder_that_does_not_divide_evenly_lands_entirely_on_the_last_installment()
    {
        // financed = 1,000,000; base = floor(1,000,000 / 3) = 333,333; last = 1,000,000 - 333,333*2 = 333,334.
        var amounts = InstallmentAmountCalculator.CalculateAmounts(
            totalPremium: 1_000_000m, downPayment: 0m, installmentCount: 3);

        Assert.Equal([333_333m, 333_333m, 333_334m], amounts);
        Assert.Equal(1_000_000m, amounts.Sum());
    }

    [Fact]
    public void Zero_or_negative_installment_count_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InstallmentAmountCalculator.CalculateAmounts(1_000_000m, 0m, 0));
    }
}
