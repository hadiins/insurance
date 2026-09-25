using System.Globalization;

namespace Aqsat.Application.Today;

/// <summary>
/// The arithmetic behind the Today dashboard's figures — kept as pure functions so every date and
/// money rule here is unit-testable without a database (CLAUDE.md: "Unit test for any date or money
/// calculation").
/// </summary>
public static class TodayMath
{
    private const int TrendLookbackDays = 7;

    /// <summary>Days in the Jalali month the given date falls in. Uses the BCL's PersianCalendar —
    /// no extra package, and the Iranian month lengths (۱–۳۰/۳۱, اسفند ۲۹/۳۰) come out right in leap
    /// years without a hand-maintained table.</summary>
    public static int DaysInJalaliMonth(DateOnly date)
    {
        var (year, month) = JalaliMonth(date);
        return new PersianCalendar().GetDaysInMonth(year, month);
    }

    /// <summary>The Jalali month's first day, as a civil date — the window start for "collected this
    /// month".</summary>
    public static DateOnly StartOfJalaliMonth(DateOnly date)
    {
        var (year, month) = JalaliMonth(date);
        return DateOnly.FromDateTime(new PersianCalendar().ToDateTime(year, month, 1, 0, 0, 0, 0));
    }

    /// <summary>How much of the monthly goal has been collected, as a percentage. Null when no goal
    /// is configured — a percentage of an unset target is not zero, it is unknown.</summary>
    public static decimal? MonthProgressPercent(decimal collected, decimal? monthlyGoal)
    {
        if (monthlyGoal is not > 0)
        {
            return null;
        }

        return decimal.Round(collected / monthlyGoal.Value * 100m, 1, MidpointRounding.AwayFromZero);
    }

    private static (int Year, int Month) JalaliMonth(DateOnly date)
    {
        var calendar = new PersianCalendar();
        var moment = date.ToDateTime(TimeOnly.MinValue);
        return (calendar.GetYear(moment), calendar.GetMonth(moment));
    }

    /// <summary>The agency's daily collection target: its monthly goal spread evenly over the
    /// current Jalali month. Null when no goal is configured — the dashboard then draws no goal bar
    /// at all rather than a fabricated zero target.</summary>
    public static decimal? DailyTarget(decimal? monthlyGoal, DateOnly today)
    {
        if (monthlyGoal is not > 0)
        {
            return null;
        }

        return decimal.Round(monthlyGoal.Value / DaysInJalaliMonth(today), 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>How far today's figure moved against the mean of the previous seven days, as a
    /// percentage. Null when the baseline is zero (there is no percentage of nothing) — the UI shows
    /// «—» rather than an invented 100%.</summary>
    public static decimal? DeltaPercent(decimal todayValue, decimal previousSevenDayTotal)
    {
        var baseline = previousSevenDayTotal / TrendLookbackDays;
        if (baseline == 0m)
        {
            return null;
        }

        return decimal.Round((todayValue - baseline) / baseline * 100m, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>How much of that day's goal was still outstanding — never negative, so a day that
    /// beat its target shows no goal bar rather than an inverted one.</summary>
    public static decimal GoalRemaining(decimal collected, decimal? dailyTarget)
    {
        if (dailyTarget is not > 0)
        {
            return 0m;
        }

        var remaining = dailyTarget.Value - collected;
        return remaining > 0 ? remaining : 0m;
    }
}
