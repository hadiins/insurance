using Aqsat.Domain.Enums;

namespace Aqsat.Api.Contracts;

/// <summary>
/// DTOs for the platform-owner monitoring & security dashboard (api/monitoring/*). Everything is
/// Platform.Owner-only; the frontend consumes exactly these shapes across its four pages.
/// </summary>
public sealed record MonitoringOverviewDto(
    long TotalRequests,
    long TotalErrors,
    long TotalClientErrors,
    double ErrorRatePercent,
    int LatencyP95Ms,
    int LatencyP99Ms,
    double UptimePercent,
    string HealthStatus,
    DateTimeOffset? LastSampleUtc,
    int ActiveAlerts,
    List<AlertOccurrenceDto> LatestAlerts,
    SecuritySummaryDto Security);

public sealed record SecuritySummaryDto(
    int FailedLogins,
    int SuccessfulLogins,
    int RateLimitRejections,
    int PermissionDenied,
    int SensitiveSettingChanges,
    int SuspiciousActivities,
    int CriticalEvents,
    int WarningEvents);

public sealed record TimeseriesPointDto(
    DateTimeOffset BucketUtc,
    long Requests,
    long Errors,
    long ClientErrors,
    int LatencyP95Ms,
    int LatencyP99Ms);

public sealed record EndpointStatDto(
    string Path,
    long Count,
    long Errors,
    double AvgMs,
    long SlowCount);

public sealed record SlowQueryDto(
    string Text,
    long Count,
    double AvgMs,
    double MaxMs);

public sealed record ServerStatusDto(
    double? CpuPercent,
    double ProcessMemoryMb,
    int ThreadCount,
    double DiskFreeGb,
    int? DbProbeMs,
    string HealthStatus,
    double ProcessUpMinutes,
    int LiveMinuteRequests,
    int LiveMinuteErrors,
    HttpsStatusDto Https);

public sealed record HttpsStatusDto(bool Enabled, string Url, string Status);

public sealed record RecurringJobStatusDto(
    string Id,
    string Cron,
    DateTimeOffset? LastExecutionUtc,
    DateTimeOffset? NextExecutionUtc);

public sealed record HangfireStatsDto(
    long FailedCount,
    long ProcessingCount,
    long ScheduledCount,
    int ServersCount,
    List<RecurringJobStatusDto> Jobs);

public sealed record LogEntryDto(
    DateTimeOffset TimestampUtc,
    string Level,
    string Message);

/// <summary>Page of parsed Serilog lines plus the cursor the next page needs (the timestamp of the
/// oldest entry returned — pass back as `before`).</summary>
public sealed record LogPageDto(
    List<LogEntryDto> Entries,
    DateTimeOffset? NextBefore,
    bool HasMore);

public sealed record SecurityEventDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Type,
    string Severity,
    string? IpAddress,
    string? Mobile,
    string Detail);

public sealed record SecurityEventPageDto(
    List<SecurityEventDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record SecurityScoreDto(
    int Score,
    List<SecurityScoreFactorDto> Factors);

public sealed record SecurityScoreFactorDto(
    string Label,
    int Impact,
    bool Ok);

public sealed record AlertOccurrenceDto(
    Guid Id,
    string RuleName,
    string Severity,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset LastSeenAt,
    double ObservedValue,
    string Message);

public sealed record AlertRuleDto(
    Guid Id,
    string Name,
    string Metric,
    string Comparator,
    double Threshold,
    int WindowMinutes,
    string Severity,
    bool IsEnabled,
    bool SmsNotify);

public sealed record CreateAlertRuleRequest(
    string Name,
    string Metric,
    string Comparator,
    double Threshold,
    int WindowMinutes,
    string Severity,
    bool IsEnabled,
    bool SmsNotify);

public sealed record UpdateAlertRuleRequest(
    string Name,
    string Metric,
    string Comparator,
    double Threshold,
    int WindowMinutes,
    string Severity,
    bool IsEnabled,
    bool SmsNotify);

/// <summary>Parsing shape for the raw-SQL timeseries aggregation (SqlQuery&lt;T&gt; requires a
/// settable class; DATEADD returns datetime, so the bucket arrives as DateTime and is converted to
/// UTC DateTimeOffset at the DTO boundary).</summary>
public sealed class TimeseriesBucketRow
{
    public DateTime BucketUtc { get; set; }
    public long Requests { get; set; }
    public long Errors { get; set; }
    public long ClientErrors { get; set; }
    public int LatencyP95Ms { get; set; }
    public int LatencyP99Ms { get; set; }
}
