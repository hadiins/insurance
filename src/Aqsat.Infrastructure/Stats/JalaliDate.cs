namespace Aqsat.Infrastructure.Stats;

/// <summary>
/// Gregorian→Jalali conversion for stats bucketing only — display formatting stays in the
/// frontend. The standard div/mod algorithm (same one jalaali-js implements), valid for all
/// dates in the operational range.
/// </summary>
internal static class JalaliDate
{
    private static readonly int[] GregorianDayOfMonths = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];

    public static (int Year, int Month) ToJalaliMonth(DateOnly date)
    {
        var (year, month, _) = ToJalali(date);
        return (year, month);
    }

    /// <summary>Months since the Jalali epoch as a single monotonic key — comparing/subtracting
    /// months without calendar arithmetic.</summary>
    public static int MonthKey(DateOnly date)
    {
        var (year, month) = ToJalaliMonth(date);
        return year * 12 + (month - 1);
    }

    public static (int Year, int Month, int Day) ToJalali(DateOnly date)
    {
        var gy = date.Year;
        var gm = date.Month;
        var gd = date.Day;

        var jy = gy <= 1600 ? 0 : 979;
        gy -= gy <= 1600 ? 621 : 1600;
        var gy2 = gm > 2 ? gy + 1 : gy;
        var days = (365 * gy) + ((gy2 + 3) / 4) - ((gy2 + 99) / 100) + ((gy2 + 399) / 400) - 80
            + gd + GregorianDayOfMonths[gm - 1];

        jy += 33 * (days / 12053);
        days %= 12053;
        jy += 4 * (days / 1461);
        days %= 1461;
        if (days > 365)
        {
            jy += (days - 1) / 365;
            days = (days - 1) % 365;
        }

        var jm = days < 186 ? 1 + (days / 31) : 7 + ((days - 186) / 30);
        var jd = 1 + (days < 186 ? days % 31 : (days - 186) % 30);
        return (jy, jm, jd);
    }
}
