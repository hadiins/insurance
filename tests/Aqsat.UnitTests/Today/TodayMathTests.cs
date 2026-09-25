using System.Globalization;
using Aqsat.Application.Today;

namespace Aqsat.UnitTests.Today;

/// <summary>
/// The date and money arithmetic behind the Today dashboard. CLAUDE.md: "Unit test for any date or
/// money calculation" — so these run against the pure functions, with no database and no seeded
/// data, and pin the Jalali month lengths against real Gregorian dates rather than re-deriving them
/// from the same calendar the code under test uses.
///
/// Every date is parsed with <see cref="CultureInfo.InvariantCulture"/> on purpose: these tests do
/// not boot Program.Main, which is where the app forces the invariant culture process-wide, so an
/// unqualified Parse here would silently read "2024-03-20" through whatever calendar the host's OS
/// locale uses — on a Persian-locale machine, the Persian one, turning it into a date 620 years
/// away.
/// </summary>
public class TodayMathTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso, CultureInfo.InvariantCulture);

    // Months 1–6 of the Jalali year are 31 days, 7–11 are 30, and اسفند is 29 or 30 depending on
    // the leap year — verified against the real Nowruz boundaries:
    //   2024-03-20 = 1403/01/01 · 2024-08-22 = 1403/06/01 · 2024-10-22 = 1403/08/01
    //   2025-02-19 = 1403/12/01 (1403 is a leap year, so اسفند has 30 days)
    //   2026-02-20 = 1404/12/01 (1404 is not, so اسفند has 29)
    [Theory]
    [InlineData("2024-03-20", 31)]
    [InlineData("2024-09-21", 31)]  // last day of month 6
    [InlineData("2024-10-22", 30)]  // month 8
    [InlineData("2025-02-20", 30)]  // leap اسفند
    [InlineData("2026-02-20", 29)]  // ordinary اسفند
    public void DaysInJalaliMonth_follows_the_real_Jalali_month_lengths(string date, int expected)
    {
        Assert.Equal(expected, TodayMath.DaysInJalaliMonth(D(date)));
    }

    [Theory]
    [InlineData("2024-03-20", "2024-03-20")]  // 1403/01/01 — the month starts on the date itself
    [InlineData("2024-09-21", "2024-08-22")]  // 1403/06/31 — six weeks into the month
    [InlineData("2025-02-20", "2025-02-19")]  // 1403/12/02
    [InlineData("2026-02-20", "2026-02-20")]
    public void StartOfJalaliMonth_is_the_first_day_of_the_containing_month(string date, string expected)
    {
        Assert.Equal(D(expected), TodayMath.StartOfJalaliMonth(D(date)));
    }

    /// <summary>The divisor is the length of the *current* Jalali month, not a fixed 30 — a goal
    /// spread over 31 days and one spread over 29 are different daily targets.</summary>
    [Theory]
    [InlineData("2024-03-20", 1_000_000)]  // 31-day month
    [InlineData("2024-10-22", 1_033_333)]  // 30-day month
    [InlineData("2026-02-20", 1_068_966)]  // 29-day month
    public void DailyTarget_spreads_the_monthly_goal_over_the_current_Jalali_month(string date, int expected)
    {
        Assert.Equal(expected, TodayMath.DailyTarget(31_000_000m, D(date)));
    }

    [Fact]
    public void DailyTarget_is_unknown_without_a_goal()
    {
        // Not zero: a zero target would draw a goal bar at the axis and read as "the agency is
        // expected to collect nothing today". Zero and null are both "no goal configured".
        Assert.Null(TodayMath.DailyTarget(null, D("2024-03-20")));
        Assert.Null(TodayMath.DailyTarget(0m, D("2024-03-20")));
    }

    [Fact]
    public void DeltaPercent_is_unknown_when_the_previous_week_collected_nothing()
    {
        // A percentage of nothing is not 100% and not 0% — it does not exist, and the UI shows «—».
        Assert.Null(TodayMath.DeltaPercent(500_000m, 0m));
    }

    [Theory]
    [InlineData(700_000, 0)]       // exactly the seven-day mean
    [InlineData(1_400_000, 100)]   // double it
    [InlineData(350_000, -50)]     // half of it
    public void DeltaPercent_compares_today_against_the_mean_of_the_previous_seven_days(int today, int expected)
    {
        Assert.Equal(expected, TodayMath.DeltaPercent(today, 4_900_000m));
    }

    [Fact]
    public void GoalRemaining_never_goes_negative()
    {
        // A day that beat its target shows no goal bar, not an inverted one.
        Assert.Equal(0m, TodayMath.GoalRemaining(1_500_000m, 1_000_000m));
        Assert.Equal(500_000m, TodayMath.GoalRemaining(500_000m, 1_000_000m));
        Assert.Equal(0m, TodayMath.GoalRemaining(500_000m, null));
    }

    [Fact]
    public void MonthProgressPercent_is_unknown_without_a_goal()
    {
        Assert.Null(TodayMath.MonthProgressPercent(5_000_000m, null));
        Assert.Null(TodayMath.MonthProgressPercent(5_000_000m, 0m));
        Assert.Equal(50.0m, TodayMath.MonthProgressPercent(15_500_000m, 31_000_000m));
    }
}
