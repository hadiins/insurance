namespace Aqsat.Application.Schedule;

/// <summary>
/// docs/PHASE-1-SPEC.md §3.1: DueDate(n) = StartDate + n months, same day-of-month. .NET's
/// DateOnly.AddMonths already clamps day-of-month overflow onto the shorter target month (e.g.
/// Jan 31 + 1 month -> Feb 28, or Feb 29 in a leap year) — the "clamp overflow" rule in the spec
/// describes that built-in behaviour, not something to hand-roll.
/// </summary>
public static class DueDateCalculator
{
    public static DateOnly CalculateDueDate(DateOnly startDate, int installmentSeqNo)
    {
        if (installmentSeqNo <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(installmentSeqNo), "شمارهٔ قسط باید مثبت باشد.");
        }

        return startDate.AddMonths(installmentSeqNo);
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
