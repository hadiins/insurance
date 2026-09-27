using System.Globalization;

namespace Aqsat.Application.Common;

/// <summary>
/// Persian/Jalali text formatting for anything that leaves the server toward a HUMAN — today that
/// means SMS bodies. Every date in this system is stored and compared as a Gregorian DateOnly (see
/// IranClock), so a naive ToString("yyyy-MM-dd") hands the customer «۲۰۲۶-۰۳-۱۷» on a Persian-locale
/// phone: a Gregorian date with Latin digits, contradicting the entire UI, which shows Jalali
/// Persian digits everywhere. PersianCalendar comes from the BCL — no package, and the Esfand
/// 29/30 leap handling is correct without a hand-maintained month table.
/// </summary>
public static class PersianText
{
    private static readonly PersianCalendar Calendar = new();

    /// <summary>Jalali year/month/day with Persian digits, ready to drop into Persian prose —
    /// e.g. 2026-03-17 → «۱۴۰۴/۱۲/۲۶» (the Iranian calendar, not a hand-converted guess).</summary>
    public static string JalaliDate(DateOnly gregorian) => ToPersianDigits(
        $"{Calendar.GetYear(gregorian.ToDateTime(TimeOnly.MinValue)):0000}/"
        + $"{Calendar.GetMonth(gregorian.ToDateTime(TimeOnly.MinValue)):00}/"
        + $"{Calendar.GetDayOfMonth(gregorian.ToDateTime(TimeOnly.MinValue)):00}");

    /// <summary>0-9 → ۰-۹. Applied last, to the already-assembled string, so nothing (no number
    /// format, no padding) can reintroduce Latin digits afterwards.</summary>
    public static string ToPersianDigits(string value) =>
        string.Create(value.Length, value, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c;
            }
        });
}

/// <summary>
/// The boundary between Iranian civil days. Iran abolished DST in 2022, so a fixed +3:30 offset is
/// exact. Every civil date in this system (DueDate, PaidOn, SettlementDeadline) is an Iranian day,
/// so "today" must never be the raw UTC date: between 00:00 and 03:30 Iran time those two disagree,
/// which shows a day-old dashboard, rejects a payment dated today as "in the future", and shifts
/// every countdown window by one day.
/// </summary>
public static class IranClock
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3.5);

    public static DateOnly Today() => Today(TimeProvider.System);

    public static DateOnly Today(TimeProvider timeProvider) => DayOf(timeProvider.GetUtcNow());

    public static DateOnly DayOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(Offset).DateTime);
}
