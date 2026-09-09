using Aqsat.Domain.Enums;

namespace Aqsat.Infrastructure.Monitoring;

/// <summary>
/// The aggregates of a rule's evaluation window — built by AlertEvaluationService from
/// MetricSample/SecurityEvent rows, consumed by the pure evaluator below. Nullable = "no data in
/// the window" (e.g. zero requests), which is NOT a breach of anything.
/// </summary>
public sealed record AlertWindowSnapshot(
    double? ErrorRatePercent,
    double? LatencyP95Ms,
    double? CpuPercent,
    double? ProcessMemoryMb,
    double? DbProbeMs,
    double? DiskFreeGb,
    int FailedLoginCount,
    bool HealthDown);

/// <summary>
/// Pure alert evaluation — no I/O, so the whole breach/message logic is unit-testable in isolation
/// (tests/Aqsat.UnitTests/Monitoring). Aggregation semantics per metric, deliberately chosen:
/// rates/counts aggregate over the window (a burst spread across minutes is still a burst), while
/// point-in-time resources (CPU, RAM, latency, DB probe) take the window's WORST value, because
/// "the app was healthy on average" must not hide "it seized for two minutes". Disk free inverts:
/// the window's minimum, since the low point is the dangerous one.
/// </summary>
public static class AlertEvaluator
{
    public static double? Observe(AlertMetric metric, AlertWindowSnapshot window) => metric switch
    {
        AlertMetric.ErrorRatePercent => window.ErrorRatePercent,
        AlertMetric.LatencyP95Ms => window.LatencyP95Ms,
        AlertMetric.CpuPercent => window.CpuPercent,
        AlertMetric.ProcessMemoryMb => window.ProcessMemoryMb,
        AlertMetric.DbProbeMs => window.DbProbeMs,
        AlertMetric.DiskFreeGb => window.DiskFreeGb,
        AlertMetric.FailedLoginCount => window.FailedLoginCount,
        AlertMetric.HealthDown => window.HealthDown ? 1 : 0,
        _ => null,
    };

    public static bool IsBreached(AlertComparator comparator, double threshold, double observed) =>
        comparator == AlertComparator.GreaterThan ? observed > threshold : observed < threshold;

    /// <summary>Persian message composed at write time (rule 31), shown verbatim on the alerts
    /// panel and in the SMS text.</summary>
    public static string ComposeMessage(
        string ruleName, AlertMetric metric, AlertComparator comparator, double threshold, double observed)
    {
        var direction = metric switch
        {
            AlertMetric.DiskFreeGb => "کمتر از",
            _ => comparator == AlertComparator.GreaterThan ? "بیش از" : "کمتر از",
        };
        var formatted = metric is AlertMetric.ErrorRatePercent or AlertMetric.CpuPercent
            ? observed.ToString("0.##")
            : observed.ToString("0");
        return $"هشدار «{ruleName}»: {MetricNameFa(metric)} {direction} {FormatThreshold(metric, threshold)} — مقدار دیده‌شده {formatted} {MetricUnitFa(metric)}";
    }

    private static string FormatThreshold(AlertMetric metric, double threshold) =>
        metric is AlertMetric.ErrorRatePercent or AlertMetric.CpuPercent
            ? threshold.ToString("0.##")
            : threshold.ToString("0");

    public static string MetricNameFa(AlertMetric metric) => metric switch
    {
        AlertMetric.ErrorRatePercent => "نرخ خطای سرور",
        AlertMetric.LatencyP95Ms => "تأخیر پاسخ (P95)",
        AlertMetric.CpuPercent => "پردازش فرایند",
        AlertMetric.ProcessMemoryMb => "حافظهٔ فرایند",
        AlertMetric.DbProbeMs => "زمان پاسخ دیتابیس",
        AlertMetric.DiskFreeGb => "فضای آزاد دیسک",
        AlertMetric.FailedLoginCount => "ورودهای ناموفق",
        AlertMetric.HealthDown => "از دسترس خارج شدن سرویس",
        _ => metric.ToString(),
    };

    public static string MetricUnitFa(AlertMetric metric) => metric switch
    {
        AlertMetric.ErrorRatePercent or AlertMetric.CpuPercent => "درصد",
        AlertMetric.LatencyP95Ms or AlertMetric.DbProbeMs => "میلی‌ثانیه",
        AlertMetric.ProcessMemoryMb => "مگابایت",
        AlertMetric.DiskFreeGb => "گیگابایت",
        AlertMetric.FailedLoginCount => "مورد",
        _ => string.Empty,
    };
}
