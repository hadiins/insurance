using System.Globalization;
using Aqsat.Application.Schedule;
using Aqsat.Infrastructure.Schedule;

namespace Aqsat.UnitTests.Schedule;

/// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §8 — the whole point of these cases is that they'd
/// come out wrong under Gregorian AddMonths. Dates are built via PersianCalendar itself rather than
/// hand-converted Gregorian literals, so the test can't accidentally encode the same conversion bug
/// it's meant to catch.</summary>
public class DueDateCalculatorTests
{
    private static readonly PersianCalendar Persian = new();

    private static DateOnly Jalali(int year, int month, int day) =>
        DateOnly.FromDateTime(Persian.ToDateTime(year, month, day, 0, 0, 0, 0));

    [Fact]
    public void Due_date_is_start_date_plus_n_months_same_day()
    {
        var start = Jalali(1405, 3, 15);
        Assert.Equal(Jalali(1405, 4, 15), DueDateCalculator.CalculateDueDate(start, 1));
        Assert.Equal(Jalali(1405, 9, 15), DueDateCalculator.CalculateDueDate(start, 6));
    }

    [Fact]
    public void Thirtyfirst_of_shahrivar_plus_one_month_clamps_to_thirtieth_of_mehr()
    {
        // docs/TASK-25-IDENTITY-VEHICLE.md §6.2/§8 give this trap case as "31 مرداد + 1 ماه = 30
        // شهریور", but PersianCalendar (verified directly, not assumed) says Mordad AND Shahrivar
        // are both 31-day months — months 1-6 all are, only 7-11 (30) and 12 (29/30) are shorter.
        // The genuine clamp the doc is illustrating sits one month later, at the 6→7 boundary.
        var start = Jalali(1405, 6, 31);
        Assert.Equal(Jalali(1405, 7, 30), DueDateCalculator.CalculateDueDate(start, 1));
    }

    [Fact]
    public void Thirtyfirst_of_farvardin_plus_one_month_stays_the_thirtyfirst_of_ordibehesht()
    {
        // Both Farvardin and Ordibehesht have 31 days — no clamping needed here.
        var start = Jalali(1405, 1, 31);
        Assert.Equal(Jalali(1405, 2, 31), DueDateCalculator.CalculateDueDate(start, 1));
    }

    [Fact]
    public void Thirtieth_of_aban_plus_three_months_lands_on_thirtieth_of_bahman()
    {
        var start = Jalali(1405, 8, 30);
        Assert.Equal(Jalali(1405, 11, 30), DueDateCalculator.CalculateDueDate(start, 3));
    }

    [Fact]
    public void Esfand_thirtieth_in_a_leap_year_is_handled_correctly()
    {
        // 1403 is a Jalali leap year — Esfand has 30 days. 1404 is not, so the 30th clamps to 29.
        Assert.True(Persian.IsLeapYear(1403));
        Assert.False(Persian.IsLeapYear(1404));

        var leapEsfand = Jalali(1403, 12, 30);
        Assert.Equal(Jalali(1404, 1, 30), DueDateCalculator.CalculateDueDate(leapEsfand, 1));

        var start = Jalali(1404, 11, 30);
        Assert.Equal(Jalali(1404, 12, 29), DueDateCalculator.CalculateDueDate(start, 1));
    }

    [Fact]
    public void Zero_or_negative_seqno_is_rejected()
    {
        var start = new DateOnly(2026, 1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => DueDateCalculator.CalculateDueDate(start, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DueDateCalculator.CalculateDueDate(start, -1));
    }

    [Fact]
    public async Task Deadline_shifts_past_a_holiday_but_due_date_never_moves()
    {
        // 2026-08-20 is a Thursday; 2026-08-21 is a Friday (verified against a real calendar,
        // not assumed) — the case this test is built around.
        var dueDate = new DateOnly(2026, 8, 20);
        var holidayChecker = new WeekendOnlyHolidayChecker();

        // +3 days lands on Sunday Aug 23 — not a Friday, no shift needed.
        var deadline = await DueDateCalculator.CalculateSettlementDeadlineAsync(dueDate, 3, shiftOnHoliday: true, holidayChecker);
        Assert.Equal(new DateOnly(2026, 8, 23), deadline);

        // +1 day lands on Friday Aug 21 — must shift to Saturday Aug 22.
        var deadlineOnFriday = await DueDateCalculator.CalculateSettlementDeadlineAsync(dueDate, 1, shiftOnHoliday: true, holidayChecker);
        Assert.Equal(new DateOnly(2026, 8, 22), deadlineOnFriday);

        // Without shiftOnHoliday, the deadline is never moved even if it lands on a holiday.
        var unshifted = await DueDateCalculator.CalculateSettlementDeadlineAsync(dueDate, 1, shiftOnHoliday: false, holidayChecker);
        Assert.Equal(new DateOnly(2026, 8, 21), unshifted);
    }
}
