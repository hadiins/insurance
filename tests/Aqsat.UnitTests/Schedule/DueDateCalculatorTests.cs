using Aqsat.Application.Schedule;
using Aqsat.Infrastructure.Schedule;

namespace Aqsat.UnitTests.Schedule;

/// <summary>Task 8's own check (docs/TASKS.md): "unit tests for due dates including 31st-of-month
/// overflow and leap years."</summary>
public class DueDateCalculatorTests
{
    [Fact]
    public void Due_date_is_start_date_plus_n_months_same_day()
    {
        var start = new DateOnly(2026, 3, 15);
        Assert.Equal(new DateOnly(2026, 4, 15), DueDateCalculator.CalculateDueDate(start, 1));
        Assert.Equal(new DateOnly(2026, 9, 15), DueDateCalculator.CalculateDueDate(start, 6));
    }

    [Fact]
    public void Overflow_past_the_31st_clamps_to_the_shorter_months_last_day()
    {
        // Jan 31 + 1 month has no Feb 31 — clamps to Feb 28 (2026 is not a leap year).
        var start = new DateOnly(2026, 1, 31);
        Assert.Equal(new DateOnly(2026, 2, 28), DueDateCalculator.CalculateDueDate(start, 1));

        // Jan 31 + 3 months -> Apr 30 (April has 30 days).
        Assert.Equal(new DateOnly(2026, 4, 30), DueDateCalculator.CalculateDueDate(start, 3));
    }

    [Fact]
    public void Leap_year_february_is_handled_correctly()
    {
        // 2028 is a leap year — Jan 31 + 1 month -> Feb 29, not Feb 28.
        var start = new DateOnly(2028, 1, 31);
        Assert.Equal(new DateOnly(2028, 2, 29), DueDateCalculator.CalculateDueDate(start, 1));

        // 2027 is not a leap year — same input clamps to Feb 28.
        var nonLeapStart = new DateOnly(2027, 1, 31);
        Assert.Equal(new DateOnly(2027, 2, 28), DueDateCalculator.CalculateDueDate(nonLeapStart, 1));
    }

    [Fact]
    public void Zero_or_negative_seqno_is_rejected()
    {
        var start = new DateOnly(2026, 1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => DueDateCalculator.CalculateDueDate(start, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DueDateCalculator.CalculateDueDate(start, -1));
    }

    [Fact]
    public void Deadline_shifts_past_a_holiday_but_due_date_never_moves()
    {
        // 2026-08-20 is a Thursday; 2026-08-21 is a Friday (verified against a real calendar,
        // not assumed) — the case this test is built around.
        var dueDate = new DateOnly(2026, 8, 20);
        var holidayChecker = new WeekendOnlyHolidayChecker();

        // +3 days lands on Sunday Aug 23 — not a Friday, no shift needed.
        var deadline = DueDateCalculator.CalculateSettlementDeadline(dueDate, 3, shiftOnHoliday: true, holidayChecker);
        Assert.Equal(new DateOnly(2026, 8, 23), deadline);

        // +1 day lands on Friday Aug 21 — must shift to Saturday Aug 22.
        var deadlineOnFriday = DueDateCalculator.CalculateSettlementDeadline(dueDate, 1, shiftOnHoliday: true, holidayChecker);
        Assert.Equal(new DateOnly(2026, 8, 22), deadlineOnFriday);

        // Without shiftOnHoliday, the deadline is never moved even if it lands on a holiday.
        var unshifted = DueDateCalculator.CalculateSettlementDeadline(dueDate, 1, shiftOnHoliday: false, holidayChecker);
        Assert.Equal(new DateOnly(2026, 8, 21), unshifted);
    }
}
