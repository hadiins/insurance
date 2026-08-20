namespace Aqsat.Application.Countdown;

/// <summary>
/// docs/PHASE-1-SPEC.md §3.3: overdue (deadline passed, unsettled) · critical (0–1 days left in the
/// collection window) · warning (2–3) · upcoming (due date not yet reached, window not open) ·
/// future (not due within the near horizon). The window is [DueDate, SettlementDeadline] — the 3
/// days the agent has to remit after an installment falls due.
/// </summary>
public static class CountdownUrgencyClassifier
{
    private const int NearHorizonDays = 7;

    public static CountdownUrgency Classify(DateOnly today, DateOnly dueDate, DateOnly settlementDeadline)
    {
        if (today > settlementDeadline)
        {
            return CountdownUrgency.Overdue;
        }

        if (today >= dueDate)
        {
            var daysRemaining = settlementDeadline.DayNumber - today.DayNumber;
            return daysRemaining <= 1 ? CountdownUrgency.Critical : CountdownUrgency.Warning;
        }

        var daysUntilDue = dueDate.DayNumber - today.DayNumber;
        return daysUntilDue <= NearHorizonDays ? CountdownUrgency.Upcoming : CountdownUrgency.Future;
    }
}
