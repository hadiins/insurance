using Aqsat.Application.Reports;

namespace Aqsat.UnitTests.Reports;

public class PnlTotalsTests
{
    [Fact]
    public void Net_profit_is_total_income_minus_total_expense()
    {
        var totals = new PnlTotals(AgencyCommissionIncome: 1_000_000m, ServiceFeeIncome: 200_000m, MarketerCommissionExpense: 300_000m, DefaultWriteOffExpense: 50_000m);

        Assert.Equal(1_200_000m, totals.TotalIncome);
        Assert.Equal(350_000m, totals.TotalExpense);
        Assert.Equal(850_000m, totals.NetProfit);
    }

    [Fact]
    public void Adding_two_totals_sums_each_of_the_four_parts_independently()
    {
        var a = new PnlTotals(100m, 10m, 5m, 1m);
        var b = new PnlTotals(200m, 20m, 15m, 4m);

        var sum = a + b;

        Assert.Equal(300m, sum.AgencyCommissionIncome);
        Assert.Equal(30m, sum.ServiceFeeIncome);
        Assert.Equal(20m, sum.MarketerCommissionExpense);
        Assert.Equal(5m, sum.DefaultWriteOffExpense);
    }
}
