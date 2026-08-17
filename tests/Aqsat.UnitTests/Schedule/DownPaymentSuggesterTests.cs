using Aqsat.Application.Schedule;

namespace Aqsat.UnitTests.Schedule;

public class DownPaymentSuggesterTests
{
    [Fact]
    public void Suggested_down_payment_makes_the_base_installment_a_round_number()
    {
        // premium 10,700,000 / 9 ~= 1,188,888 -> rounded down to nearest 10,000 = 1,180,000.
        // financed = 1,180,000 * 9 = 10,620,000 -> down payment = 80,000.
        var suggestion = DownPaymentSuggester.Suggest(totalPremium: 10_700_000m, installmentCount: 9);

        Assert.Equal(80_000m, suggestion);

        var amounts = InstallmentAmountCalculator.CalculateAmounts(10_700_000m, suggestion, 9);
        Assert.All(amounts, a => Assert.Equal(1_180_000m, a));
    }

    [Fact]
    public void Never_suggests_a_negative_down_payment()
    {
        var suggestion = DownPaymentSuggester.Suggest(totalPremium: 5_000m, installmentCount: 9);
        Assert.True(suggestion >= 0);
    }
}
