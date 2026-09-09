using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Monitoring;

namespace Aqsat.UnitTests.Monitoring;

/// <summary>
/// The alert evaluator is pure by design — these tests pin the semantics the sampler relies on:
/// null means "no data in the window" (never a breach), comparator direction, and the Persian
/// message composed at write time (rule 31).
/// </summary>
public class AlertEvaluatorTests
{
    private static readonly AlertWindowSnapshot Empty = new(
        ErrorRatePercent: null, LatencyP95Ms: null, CpuPercent: null, ProcessMemoryMb: null,
        DbProbeMs: null, DiskFreeGb: null, FailedLoginCount: 0, HealthDown: false);

    [Fact]
    public void A_null_observation_is_never_a_breach_no_matter_the_threshold()
    {
        // Zero requests in the window → ErrorRatePercent is null, not zero — silence must not page
        // the owner as "0% errors" nor trip a LessThan rule.
        Assert.Null(AlertEvaluator.Observe(AlertMetric.ErrorRatePercent, Empty));
        Assert.Null(AlertEvaluator.Observe(AlertMetric.DbProbeMs, Empty));
        Assert.False(AlertEvaluator.IsBreached(AlertComparator.GreaterThan, 5, 0)
            && AlertEvaluator.Observe(AlertMetric.ErrorRatePercent, Empty) is not null);
    }

    [Fact]
    public void Comparators_direct_the_comparison_in_both_directions()
    {
        Assert.True(AlertEvaluator.IsBreached(AlertComparator.GreaterThan, 5, 5.1));
        Assert.False(AlertEvaluator.IsBreached(AlertComparator.GreaterThan, 5, 5));
        Assert.True(AlertEvaluator.IsBreached(AlertComparator.LessThan, 5, 4.9));
        Assert.False(AlertEvaluator.IsBreached(AlertComparator.LessThan, 5, 5));
    }

    [Fact]
    public void HealthDown_observes_as_a_number_so_both_comparators_work()
    {
        var down = Empty with { HealthDown = true };
        Assert.Equal(1, AlertEvaluator.Observe(AlertMetric.HealthDown, down));
        Assert.Equal(0, AlertEvaluator.Observe(AlertMetric.HealthDown, Empty));
    }

    [Fact]
    public void Every_metric_maps_to_its_snapshot_field()
    {
        var snapshot = new AlertWindowSnapshot(
            ErrorRatePercent: 12.5, LatencyP95Ms: 3400, CpuPercent: 91, ProcessMemoryMb: 1800,
            DbProbeMs: 5200, DiskFreeGb: 3.2, FailedLoginCount: 14, HealthDown: false);

        Assert.Equal(12.5, AlertEvaluator.Observe(AlertMetric.ErrorRatePercent, snapshot));
        Assert.Equal(3400, AlertEvaluator.Observe(AlertMetric.LatencyP95Ms, snapshot));
        Assert.Equal(91, AlertEvaluator.Observe(AlertMetric.CpuPercent, snapshot));
        Assert.Equal(1800, AlertEvaluator.Observe(AlertMetric.ProcessMemoryMb, snapshot));
        Assert.Equal(5200, AlertEvaluator.Observe(AlertMetric.DbProbeMs, snapshot));
        Assert.Equal(3.2, AlertEvaluator.Observe(AlertMetric.DiskFreeGb, snapshot));
        Assert.Equal(14, AlertEvaluator.Observe(AlertMetric.FailedLoginCount, snapshot));
    }

    [Fact]
    public void The_message_names_the_rule_and_carries_the_observed_value()
    {
        var message = AlertEvaluator.ComposeMessage(
            "تأخیر پاسخ بالا", AlertMetric.LatencyP95Ms, AlertComparator.GreaterThan, 3000, 3482);

        Assert.Contains("تأخیر پاسخ بالا", message);
        Assert.Contains("بیش از", message);
        Assert.Contains("3000", message);
        Assert.Contains("3482", message);
        Assert.Contains("میلی‌ثانیه", message);
    }

    [Fact]
    public void Disk_free_reads_as_a_low_threshold_even_with_the_greater_comparator()
    {
        // The seeded disk rule keeps its LessThan comparator, but the message must say «کمتر از»
        // either way — "disk free above 5GB" as an alert text would be nonsense.
        var message = AlertEvaluator.ComposeMessage(
            "فضای دیسک", AlertMetric.DiskFreeGb, AlertComparator.LessThan, 5, 3.1);
        Assert.Contains("کمتر از", message);
        Assert.DoesNotContain("بیش از", message);
    }
}
