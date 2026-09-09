using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Domain.Enums;
using Aqsat.Domain.Monitoring;
using Aqsat.Infrastructure.Monitoring;
using Aqsat.Infrastructure.Persistence;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// The platform-owner monitoring &amp; security dashboard's backend (plan «پایش و امنیت», 2026-09-08).
/// Platform.Owner-only like every platform panel: agency users never learn this exists. All data is
/// real in-process telemetry — MetricSample/EndpointStat rows the Hangfire sampler persists,
/// SecurityEvents written at their sources, Serilog files, Hangfire's own monitoring API, and a live
/// process probe. Nothing here is agency-scoped, so none of it sits behind the RLS policy.
/// </summary>
[ApiController]
[Route("api/monitoring")]
[Authorize(Policy = Permissions.PlatformOwner)]
public sealed class MonitoringController(
    AppDbContext dbContext,
    RequestMetricsAggregator aggregator,
    EfCommandDurationStats efCommandStats,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ControllerBase
{
    private static readonly TimeSpan DefaultRange = TimeSpan.FromHours(1);

    // GET overview?range=1h — the dashboard's headline cards plus its alert/security summaries.
    [HttpGet("overview")]
    public async Task<ActionResult<MonitoringOverviewDto>> Overview(string? range, CancellationToken ct)
    {
        var from = DateTimeOffset.UtcNow - ParseRange(range);

        var totals = await dbContext.MetricSamples.AsNoTracking()
            .Where(s => s.MinuteUtc >= from)
            .GroupBy(_ => true)
            .Select(g => new
            {
                Requests = g.Sum(s => (long)s.Requests),
                Errors = g.Sum(s => (long)s.Errors),
                ClientErrors = g.Sum(s => (long)s.ClientErrors),
                P95 = g.Max(s => (int?)s.LatencyP95Ms) ?? 0,
                P99 = g.Max(s => (int?)s.LatencyP99Ms) ?? 0,
                Samples = g.Count(),
                DownSamples = g.Count(s => s.HealthStatus == "down"),
            })
            .FirstOrDefaultAsync(ct);

        var latest = await dbContext.MetricSamples.AsNoTracking()
            .OrderByDescending(s => s.MinuteUtc)
            .Select(s => new { s.MinuteUtc, s.HealthStatus })
            .FirstOrDefaultAsync(ct);

        var security = await SecuritySummaryAsync(from, ct);
        var latestAlerts = await AlertOccurrencesQuery()
            .OrderByDescending(o => o.LastSeenAt)
            .Take(5)
            .Select(o => new AlertOccurrenceDto(
                o.Id, o.Rule.Name, o.Rule.Severity.ToString(), o.Status.ToString(),
                o.StartedAt, o.LastSeenAt, o.ObservedValue, o.Message))
            .ToListAsync(ct);
        var activeAlerts = await dbContext.AlertOccurrences
            .CountAsync(o => o.Status != AlertStatus.Resolved, ct);

        return Ok(new MonitoringOverviewDto(
            totals?.Requests ?? 0,
            totals?.Errors ?? 0,
            totals?.ClientErrors ?? 0,
            totals is { Requests: > 0 } ? Math.Round(totals.Errors * 100.0 / totals.Requests, 2) : 0,
            totals?.P95 ?? 0,
            totals?.P99 ?? 0,
            totals is { Samples: > 0 } ? Math.Round((totals.Samples - totals.DownSamples) * 100.0 / totals.Samples, 2) : 100,
            latest?.HealthStatus ?? "unknown",
            latest?.MinuteUtc,
            activeAlerts,
            latestAlerts,
            security));
    }

    // GET timeseries?range — the dashboard's charts. Bucket size grows with the range so the point
    // count stays chartable (~60-360) instead of 43k for a 30-day minute series.
    [HttpGet("timeseries")]
    public async Task<ActionResult<List<TimeseriesPointDto>>> Timeseries(string? range, CancellationToken ct)
    {
        var (from, stepMinutes) = ParseRangeWithStep(range);

        // SqlQueryRaw with the step INLINED, not interpolated: EF gives every interpolation hole
        // its own parameter, and SQL Server then sees two different expressions ("/ @p0" vs
        // "/ @p1") and rejects the SELECT as not covered by the GROUP BY. The step is an int from
        // ParseRangeWithStep's fixed map, so inlining it carries no injection surface.
        var sql = $"""
            SELECT
                DATEADD(MINUTE, DATEDIFF(MINUTE, 0, MinuteUtc) / {stepMinutes}, 0) AS BucketUtc,
                SUM(CONVERT(bigint, Requests)) AS Requests,
                SUM(CONVERT(bigint, Errors)) AS Errors,
                SUM(CONVERT(bigint, ClientErrors)) AS ClientErrors,
                MAX(LatencyP95Ms) AS LatencyP95Ms,
                MAX(LatencyP99Ms) AS LatencyP99Ms
            FROM MetricSamples
            WHERE MinuteUtc >= @from
            GROUP BY DATEADD(MINUTE, DATEDIFF(MINUTE, 0, MinuteUtc) / {stepMinutes}, 0)
            ORDER BY BucketUtc
            """;

        var rows = await dbContext.Database
            .SqlQueryRaw<TimeseriesBucketRow>(sql, new Microsoft.Data.SqlClient.SqlParameter("@from", from))
            .ToListAsync(ct);

        return Ok(rows
            .Select(r => new TimeseriesPointDto(
                new DateTimeOffset(DateTime.SpecifyKind(r.BucketUtc, DateTimeKind.Utc), TimeSpan.Zero),
                r.Requests, r.Errors, r.ClientErrors, r.LatencyP95Ms, r.LatencyP99Ms))
            .ToList());
    }

    // GET endpoints?range — per-route-template traffic table.
    [HttpGet("endpoints")]
    public async Task<ActionResult<List<EndpointStatDto>>> Endpoints(string? range, CancellationToken ct)
    {
        var from = DateTimeOffset.UtcNow - ParseRange(range);

        var rows = await dbContext.EndpointStats.AsNoTracking()
            .Where(e => e.MinuteUtc >= from)
            .GroupBy(e => e.Path)
            .Select(g => new
            {
                Path = g.Key,
                Count = g.Sum(e => (long)e.Count),
                Errors = g.Sum(e => (long)e.Errors),
                TotalMs = g.Sum(e => (long)e.TotalMs),
                Slow = g.Sum(e => (long)e.SlowCount),
            })
            .OrderByDescending(e => e.Count)
            .Take(20)
            .ToListAsync(ct);

        return Ok(rows
            .Select(e => new EndpointStatDto(e.Path, e.Count, e.Errors, e.TotalMs / (double)Math.Max(e.Count, 1), e.Slow))
            .ToList());
    }

    // GET slow-queries — live in-memory EF command stats (reset on restart; the UI says so).
    [HttpGet("slow-queries")]
    public ActionResult<List<SlowQueryDto>> SlowQueries() =>
        Ok(efCommandStats.TopSlow(10)
            .Select(q => new SlowQueryDto(q.Text, q.Count, Math.Round(q.AvgMs, 1), Math.Round(q.MaxMs, 1)))
            .ToList());

    // GET server — live process resources from the latest persisted sample plus the in-flight
    // minute, and the public HTTPS status. Labelled "process" in the UI: shared hosting never
    // exposes whole-machine numbers and this panel does not pretend otherwise.
    [HttpGet("server")]
    public async Task<ActionResult<ServerStatusDto>> Server(CancellationToken ct)
    {
        var latest = await dbContext.MetricSamples.AsNoTracking()
            .OrderByDescending(s => s.MinuteUtc)
            .FirstOrDefaultAsync(ct);
        var (liveRequests, liveErrors) = aggregator.CurrentMinute(DateTimeOffset.UtcNow);

        return Ok(new ServerStatusDto(
            latest?.CpuPercent,
            latest?.ProcessMemoryMb ?? 0,
            latest?.ThreadCount ?? 0,
            latest?.DiskFreeGb ?? 0,
            latest?.DbProbeMs,
            latest?.HealthStatus ?? "unknown",
            latest?.ProcessUpMinutes ?? 0,
            liveRequests,
            liveErrors,
            await ProbeHttpsAsync(ct)));
    }

    // GET jobs — Hangfire's own view of itself.
    [HttpGet("jobs")]
    public ActionResult<HangfireStatsDto> Jobs()
    {
        var api = JobStorage.Current.GetMonitoringApi();

        using var connection = JobStorage.Current.GetConnection();
        var recurring = connection.GetRecurringJobs()
            .Select(j => new RecurringJobStatusDto(
                j.Id,
                j.Cron,
                ToNullableUtc(j.LastExecution),
                ToNullableUtc(j.NextExecution)))
            .OrderBy(j => j.Id, StringComparer.Ordinal)
            .ToList();

        return Ok(new HangfireStatsDto(
            api.FailedCount(),
            api.ProcessingCount(),
            api.ScheduledCount(),
            api.Servers().Count,
            recurring));
    }

    // GET logs?level&search&minutes&before&limit — Serilog tail with filters and cursor paging.
    [HttpGet("logs")]
    public ActionResult<LogPageDto> Logs(
        string? level, string? search, int? minutes, DateTimeOffset? before, int? limit)
    {
        var effectiveMinutes = Math.Clamp(minutes ?? 60, 1, 60 * 24 * 30);
        var effectiveLimit = Math.Clamp(limit ?? 100, 1, 500);

        var (lines, hasMore) = new SerilogTailReader().Tail(
            "logs", effectiveLimit, before, DateTimeOffset.UtcNow.AddMinutes(-effectiveMinutes),
            search, level);

        var entries = lines
            .Select(l => new LogEntryDto(l.TimestampUtc, l.Level, l.Message))
            .ToList();

        return Ok(new LogPageDto(
            entries,
            hasMore && entries.Count > 0 ? entries[^1].TimestampUtc : null,
            hasMore));
    }

    // GET security/events?type&severity&range&page — the security feed.
    [HttpGet("security/events")]
    public async Task<ActionResult<SecurityEventPageDto>> SecurityEvents(
        string? type, string? severity, string? range, int? page, CancellationToken ct)
    {
        var from = DateTimeOffset.UtcNow - ParseRange(range);
        var query = dbContext.SecurityEvents.AsNoTracking().AsQueryable();

        if (Enum.TryParse<SecurityEventType>(type, ignoreCase: true, out var parsedType))
        {
            query = query.Where(e => e.Type == parsedType);
        }

        if (Enum.TryParse<SecuritySeverity>(severity, ignoreCase: true, out var parsedSeverity))
        {
            query = query.Where(e => e.Severity == parsedSeverity);
        }

        var pageSize = 50;
        var effectivePage = Math.Max(page ?? 1, 1);

        var total = await query.CountAsync(e => e.OccurredAt >= from, ct);
        var items = await query
            .Where(e => e.OccurredAt >= from)
            .OrderByDescending(e => e.OccurredAt)
            .Skip((effectivePage - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new SecurityEventDto(
                e.Id, e.OccurredAt, e.Type.ToString(), e.Severity.ToString(), e.IpAddress, e.Mobile, e.Detail))
            .ToListAsync(ct);

        return Ok(new SecurityEventPageDto(items, total, effectivePage, pageSize));
    }

    // GET security/score — 0-100 with the factors that moved it, so the number is arguable, not
    // mystical.
    [HttpGet("security/score")]
    public async Task<ActionResult<SecurityScoreDto>> SecurityScore(CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-1);

        var counts = await dbContext.SecurityEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= since)
            .GroupBy(e => e.Type)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        var latestHealth = await dbContext.MetricSamples.AsNoTracking()
            .OrderByDescending(s => s.MinuteUtc)
            .Select(s => s.HealthStatus)
            .FirstOrDefaultAsync(ct);
        var activeCritical = await dbContext.AlertOccurrences
            .CountAsync(o => o.Status != AlertStatus.Resolved, ct);

        counts.TryGetValue(SecurityEventType.FailedLogin, out var failedLogins);
        counts.TryGetValue(SecurityEventType.SuspiciousActivity, out var suspicious);
        counts.TryGetValue(SecurityEventType.RateLimitRejection, out var rejected);
        var https = await ProbeHttpsAsync(ct);

        var factors = new List<SecurityScoreFactorDto>
        {
            new("اتصال امن HTTPS", https.Enabled ? 0 : 10, https.Enabled),
            new("تلاش‌های ورود ناموفق (۲۴ ساعت)", failedLogins <= 20 ? 0 : 15, failedLogins <= 20),
            new("فعالیت مشکوک شناسایی‌شده", suspicious == 0 ? 0 : 25, suspicious == 0),
            new("درخواست‌های ردشدهٔ سقف نرخ", rejected <= 100 ? 0 : 10, rejected <= 100),
            new(latestHealth switch
                {
                    "down" => "دسترس‌پذیری دیتابیس",
                    "degraded" => "کندی دیتابیس",
                    _ => "سلامت دیتابیس",
                },
                latestHealth switch { "down" => 30, "degraded" => 10, _ => 0 },
                latestHealth is not ("down" or "degraded")),
            new("هشدارهای فعال سامانه", activeCritical == 0 ? 0 : 15, activeCritical == 0),
        };

        return Ok(new SecurityScoreDto(
            Math.Clamp(100 - factors.Sum(f => f.Impact), 0, 100),
            factors));
    }

    // GET alerts — latest 100 occurrences (any status).
    [HttpGet("alerts")]
    public async Task<ActionResult<List<AlertOccurrenceDto>>> Alerts(CancellationToken ct) =>
        Ok(await AlertOccurrencesQuery()
            .OrderByDescending(o => o.LastSeenAt)
            .Take(100)
            .Select(o => new AlertOccurrenceDto(
                o.Id, o.Rule.Name, o.Rule.Severity.ToString(), o.Status.ToString(),
                o.StartedAt, o.LastSeenAt, o.ObservedValue, o.Message))
            .ToListAsync(ct));

    [HttpPut("alerts/{id:guid}/ack")]
    public async Task<IActionResult> AcknowledgeAlert(Guid id, CancellationToken ct)
    {
        var occurrence = await dbContext.AlertOccurrences.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (occurrence is null)
        {
            return NotFound();
        }

        if (occurrence.Status == AlertStatus.Active)
        {
            occurrence.Status = AlertStatus.Acknowledged;
            await dbContext.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    [HttpPut("alerts/{id:guid}/resolve")]
    public async Task<IActionResult> ResolveAlert(Guid id, CancellationToken ct)
    {
        var occurrence = await dbContext.AlertOccurrences.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (occurrence is null)
        {
            return NotFound();
        }

        if (occurrence.Status != AlertStatus.Resolved)
        {
            occurrence.Status = AlertStatus.Resolved;
            occurrence.LastSeenAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    // --- alert rules CRUD -------------------------------------------------

    [HttpGet("alert-rules")]
    public async Task<ActionResult<List<AlertRuleDto>>> AlertRules(CancellationToken ct) =>
        Ok(await dbContext.AlertRules.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => ToDto(r))
            .ToListAsync(ct));

    [HttpPost("alert-rules")]
    public async Task<ActionResult<AlertRuleDto>> CreateAlertRule(CreateAlertRuleRequest request, CancellationToken ct)
    {
        if (!TryValidateRule(request.Name, request.Metric, request.Comparator, request.Severity,
                request.Threshold, request.WindowMinutes, out var problem))
        {
            return ValidationProblem(problem);
        }

        var rule = new AlertRule
        {
            Name = request.Name!.Trim(),
            Metric = Enum.Parse<AlertMetric>(request.Metric!, ignoreCase: true),
            Comparator = Enum.Parse<AlertComparator>(request.Comparator!, ignoreCase: true),
            Threshold = request.Threshold,
            WindowMinutes = request.WindowMinutes,
            Severity = Enum.Parse<SecuritySeverity>(request.Severity!, ignoreCase: true),
            IsEnabled = request.IsEnabled,
            SmsNotify = request.SmsNotify,
        };
        dbContext.AlertRules.Add(rule);
        await dbContext.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(AlertRules), ToDto(rule));
    }

    [HttpPut("alert-rules/{id:guid}")]
    public async Task<ActionResult<AlertRuleDto>> UpdateAlertRule(
        Guid id, UpdateAlertRuleRequest request, CancellationToken ct)
    {
        if (!TryValidateRule(request.Name, request.Metric, request.Comparator, request.Severity,
                request.Threshold, request.WindowMinutes, out var problem))
        {
            return ValidationProblem(problem);
        }

        var rule = await dbContext.AlertRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null)
        {
            return NotFound();
        }

        rule.Name = request.Name!.Trim();
        rule.Metric = Enum.Parse<AlertMetric>(request.Metric!, ignoreCase: true);
        rule.Comparator = Enum.Parse<AlertComparator>(request.Comparator!, ignoreCase: true);
        rule.Threshold = request.Threshold;
        rule.WindowMinutes = request.WindowMinutes;
        rule.Severity = Enum.Parse<SecuritySeverity>(request.Severity!, ignoreCase: true);
        rule.IsEnabled = request.IsEnabled;
        rule.SmsNotify = request.SmsNotify;
        await dbContext.SaveChangesAsync(ct);

        return Ok(ToDto(rule));
    }

    [HttpDelete("alert-rules/{id:guid}")]
    public async Task<IActionResult> DeleteAlertRule(Guid id, CancellationToken ct)
    {
        var rule = await dbContext.AlertRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null)
        {
            return NotFound();
        }

        // Soft delete (rule 7). The sampler resolves any occurrence still open for this rule on
        // its next pass, so nothing lingers as Active.
        rule.IsDeleted = true;
        rule.DeletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    // --- helpers ----------------------------------------------------------

    private IQueryable<AlertOccurrence> AlertOccurrencesQuery() =>
        dbContext.AlertOccurrences.AsNoTracking();

    private async Task<SecuritySummaryDto> SecuritySummaryAsync(DateTimeOffset from, CancellationToken ct)
    {
        var byType = await dbContext.SecurityEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= from)
            .GroupBy(e => e.Type)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var bySeverity = await dbContext.SecurityEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= from)
            .GroupBy(e => e.Severity)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        return new SecuritySummaryDto(
            byType.GetValueOrDefault(SecurityEventType.FailedLogin),
            byType.GetValueOrDefault(SecurityEventType.SuccessfulLogin),
            byType.GetValueOrDefault(SecurityEventType.RateLimitRejection),
            byType.GetValueOrDefault(SecurityEventType.PermissionDenied),
            byType.GetValueOrDefault(SecurityEventType.SensitiveSettingChanged),
            byType.GetValueOrDefault(SecurityEventType.SuspiciousActivity),
            bySeverity.GetValueOrDefault(SecuritySeverity.Critical),
            bySeverity.GetValueOrDefault(SecuritySeverity.Warning));
    }

    /// <summary>Probes the configured public URL: is it HTTPS, and does it answer at all? The
    /// current production deployment is plain HTTP (memory: no HTTPS on that host), which the
    /// security panel must report honestly rather than green.</summary>
    private async Task<HttpsStatusDto> ProbeHttpsAsync(CancellationToken ct)
    {
        var publicUrl = configuration["Deployment:PublicUrl"];
        if (string.IsNullOrWhiteSpace(publicUrl) || !Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri))
        {
            return new HttpsStatusDto(false, publicUrl ?? string.Empty, "آدرس عمومی تنظیم نشده است");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var client = httpClientFactory.CreateClient();
            using var response = await client.GetAsync(uri, timeoutCts.Token);
            return new HttpsStatusDto(
                uri.Scheme == Uri.UriSchemeHttps,
                publicUrl,
                uri.Scheme == Uri.UriSchemeHttps
                    ? $"پاسخ دریافت شد ({(int)response.StatusCode})"
                    : "اتصال رمزنگاری‌شده فعال نیست (HTTP)");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new HttpsStatusDto(uri.Scheme == Uri.UriSchemeHttps, publicUrl, "سرویس پاسخ نداد");
        }
    }

    private static TimeSpan ParseRange(string? range) => range switch
    {
        "6h" => TimeSpan.FromHours(6),
        "24h" => TimeSpan.FromHours(24),
        "7d" => TimeSpan.FromDays(7),
        "30d" => TimeSpan.FromDays(30),
        _ => DefaultRange,
    };

    private static (DateTimeOffset From, int StepMinutes) ParseRangeWithStep(string? range) => range switch
    {
        "6h" => (DateTimeOffset.UtcNow.AddHours(-6), 1),
        "24h" => (DateTimeOffset.UtcNow.AddHours(-24), 10),
        "7d" => (DateTimeOffset.UtcNow.AddDays(-7), 60),
        "30d" => (DateTimeOffset.UtcNow.AddDays(-30), 360),
        _ => (DateTimeOffset.UtcNow.AddHours(-1), 1),
    };

    private static bool TryValidateRule(
        string? name, string? metric, string? comparator, string? severity,
        double threshold, int windowMinutes, out string problem)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            problem = "نام قانون الزامی است و باید حداکثر ۱۲۰ کاراکتر باشد.";
            return false;
        }

        if (!Enum.TryParse<AlertMetric>(metric, ignoreCase: true, out _))
        {
            problem = "شاخص پایش نامعتبر است.";
            return false;
        }

        if (!Enum.TryParse<AlertComparator>(comparator, ignoreCase: true, out _))
        {
            problem = "عملگر مقایسه نامعتبر است.";
            return false;
        }

        if (!Enum.TryParse<SecuritySeverity>(severity, ignoreCase: true, out _))
        {
            problem = "شدت هشدار نامعتبر است.";
            return false;
        }

        if (threshold < 0)
        {
            problem = "آستانه باید نامنفی باشد.";
            return false;
        }

        if (windowMinutes is < 1 or > 1440)
        {
            problem = "پنجرهٔ ارزیابی باید بین ۱ تا ۱۴۴۰ دقیقه باشد.";
            return false;
        }

        problem = string.Empty;
        return true;
    }

    private static AlertRuleDto ToDto(AlertRule rule) =>
        new(rule.Id, rule.Name, rule.Metric.ToString(), rule.Comparator.ToString(),
            rule.Threshold, rule.WindowMinutes, rule.Severity.ToString(), rule.IsEnabled, rule.SmsNotify);

    private static DateTimeOffset? ToNullableUtc(DateTime? value) =>
        value is { } moment ? DateTime.SpecifyKind(moment, DateTimeKind.Utc) : null;

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
