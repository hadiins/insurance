using System.Globalization;

namespace Aqsat.Application.Schedule;

/// <summary>
/// docs/PHASE-1-SPEC.md §3.1: DueDate(n) = StartDate + n months, same day-of-month.
/// docs/TASK-25-IDENTITY-VEHICLE.md §6.2 (the "critical trap", verbatim): this "+ n months" must
/// happen in the Jalali calendar, not Gregorian — Jalali months are 31/30/29 days and don't share
/// month boundaries with Gregorian ones, so DateOnly.AddMonths silently shifts dates by a day or
/// two. Against a 3-day settlement window, a one-day error is a third of the whole margin.
/// </summary>
public static class DueDateCalculator
{
    private static readonly PersianCalendar Persian = new();

    public static DateOnly CalculateDueDate(DateOnly startDate, int installmentSeqNo)
    {
        if (installmentSeqNo <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(installmentSeqNo), "شمارهٔ قسط باید مثبت باشد.");
        }

        return AddPersianMonths(startDate, installmentSeqNo);
    }

    /// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §6.2 — day-of-month overflow (e.g. the 31st into a
    /// 30- or 29-day Jalali month) clamps to that month's last day, same spirit as the Gregorian
    /// clamp this replaced.</summary>
    public static DateOnly AddPersianMonths(DateOnly start, int months)
    {
        var startDateTime = start.ToDateTime(TimeOnly.MinValue);
        var year = Persian.GetYear(startDateTime);
        var month = Persian.GetMonth(startDateTime);
        var day = Persian.GetDayOfMonth(startDateTime);

        var total = (year * 12 + (month - 1)) + months;
        var newYear = total / 12;
        var newMonth = total % 12 + 1;

        var maxDay = Persian.GetDaysInMonth(newYear, newMonth);
        var newDay = Math.Min(day, maxDay);

        return DateOnly.FromDateTime(Persian.ToDateTime(newYear, newMonth, newDay, 0, 0, 0, 0));
    }

    /// <summary>
    /// The deadline shifts on a holiday, never the due date itself (§3.1, verbatim). holidayChecker
    /// is a placeholder (Aqsat.Infrastructure.Schedule.WeekendOnlyHolidayChecker) until Task 12
    /// wires the real api.ir IsHoliday service — see docs/PHASE-1-SPEC.md §1's "Blocked" table.
    /// </summary>
    public static async Task<DateOnly> CalculateSettlementDeadlineAsync(
        DateOnly dueDate, int settlementDeadlineDays, bool shiftOnHoliday, IHolidayChecker holidayChecker, CancellationToken ct = default)
    {
        var deadline = dueDate.AddDays(settlementDeadlineDays);
        if (!shiftOnHoliday)
        {
            return deadline;
        }

        while (await holidayChecker.IsHolidayAsync(deadline, ct))
        {
            deadline = deadline.AddDays(1);
        }

        return deadline;
    }
}
