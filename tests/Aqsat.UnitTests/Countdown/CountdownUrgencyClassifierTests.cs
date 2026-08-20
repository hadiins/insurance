using Aqsat.Application.Countdown;

namespace Aqsat.UnitTests.Countdown;

/// <summary>docs/PHASE-1-SPEC.md §3.3 urgency buckets, one test per level plus the boundaries
/// between them.</summary>
public class CountdownUrgencyClassifierTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    [Fact]
    public void Deadline_already_passed_and_unsettled_is_overdue()
    {
        var dueDate = Today.AddDays(-10);
        var deadline = Today.AddDays(-1);
        Assert.Equal(CountdownUrgency.Overdue, CountdownUrgencyClassifier.Classify(Today, dueDate, deadline));
    }

    [Fact]
    public void Zero_or_one_day_left_in_an_open_window_is_critical()
    {
        var dueDate = Today.AddDays(-3);

        Assert.Equal(CountdownUrgency.Critical, CountdownUrgencyClassifier.Classify(Today, dueDate, Today));
        Assert.Equal(CountdownUrgency.Critical, CountdownUrgencyClassifier.Classify(Today, dueDate, Today.AddDays(1)));
    }

    [Fact]
    public void Two_or_three_days_left_in_an_open_window_is_warning()
    {
        var dueDate = Today.AddDays(-1);

        Assert.Equal(CountdownUrgency.Warning, CountdownUrgencyClassifier.Classify(Today, dueDate, Today.AddDays(2)));
        Assert.Equal(CountdownUrgency.Warning, CountdownUrgencyClassifier.Classify(Today, dueDate, Today.AddDays(3)));
    }

    [Fact]
    public void Due_but_not_yet_at_the_deadline_window_stays_warning_even_past_three_days()
    {
        // An agency configured with a longer SettlementDeadlineDays can still have the window open
        // with more than 3 days left — there is no sixth bucket, so it folds into "warning".
        var dueDate = Today.AddDays(-1);
        Assert.Equal(CountdownUrgency.Warning, CountdownUrgencyClassifier.Classify(Today, dueDate, Today.AddDays(5)));
    }

    [Fact]
    public void Due_within_the_next_seven_days_but_not_yet_due_is_upcoming()
    {
        var dueDate = Today.AddDays(1);
        var deadline = dueDate.AddDays(3);
        Assert.Equal(CountdownUrgency.Upcoming, CountdownUrgencyClassifier.Classify(Today, dueDate, deadline));

        var dueDateAtHorizon = Today.AddDays(7);
        Assert.Equal(
            CountdownUrgency.Upcoming,
            CountdownUrgencyClassifier.Classify(Today, dueDateAtHorizon, dueDateAtHorizon.AddDays(3)));
    }

    [Fact]
    public void Due_further_out_than_seven_days_is_future()
    {
        var dueDate = Today.AddDays(8);
        var deadline = dueDate.AddDays(3);
        Assert.Equal(CountdownUrgency.Future, CountdownUrgencyClassifier.Classify(Today, dueDate, deadline));
    }
}
