using System.Data;
using System.Diagnostics;
using Aqsat.Domain.Enums;
using Aqsat.Domain.Monitoring;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aqsat.Infrastructure.Monitoring;

/// <summary>
/// The monitoring pipeline's heartbeat (Hangfire "metrics-sampler", every minute): drains the
/// in-memory request buckets into MetricSample/EndpointStat rows, probes the process and database,
/// then hands over to AlertEvaluationService. One run failing must never take the next down with
/// it — the whole body is wrapped so a transient DB blip costs one minute of telemetry, nothing
/// more. Prunes the four telemetry tables at the end so their size stays flat forever.
/// </summary>
public sealed class MetricsSamplerJob(
    AppDbContext dbContext,
    RequestMetricsAggregator aggregator,
    AlertEvaluationService alertEvaluation,
    TimeProvider timeProvider,
    ILogger<MetricsSamplerJob> logger)
{
    // Process-wide by design even though the job itself is scoped: CPU% is a ratio of two deltas,
    // so the previous reading must survive across invocations. Lost only on restart, where the
    // first sample correctly reports "no delta yet" (CpuPercent null).
    private static DateTimeOffset? lastCpuReadUtc;
    private static TimeSpan lastTotalProcessorTime;

    private const int BruteForceThreshold = 10;
    private static readonly TimeSpan BruteForceWindow = TimeSpan.FromMinutes(15);

    public async Task RunAsync(CancellationToken ct = default)
    {
        try
        {
            var now = timeProvider.GetUtcNow();
            var drained = aggregator.DrainCompleted(now);

            var resource = await ProbeResourcesAsync(ct);

            // An idle minute (zero requests, or a job delayed into the next minute) still needs a
            // row — health, disk, DB latency and the alert rules run on every minute, traffic or
            // not. The unique index on MinuteUtc makes a re-drained minute (restart overlap) a
            // caught duplicate, never a crash (same idempotency stance as rule 24's payments).
            if (drained.Count == 0)
            {
                drained.Add(new DrainedMinute(
                    RequestMetricsAggregator.TruncateToMinute(now).AddMinutes(-1), 0, 0, 0, [], []));
            }

            var latest = drained[^1];
            foreach (var minute in drained)
            {
                dbContext.MetricSamples.Add(new MetricSample
                {
                    MinuteUtc = minute.MinuteUtc,
                    Requests = minute.Requests,
                    Errors = minute.Errors,
                    ClientErrors = minute.ClientErrors,
                    LatencyP50Ms = RequestMetricsAggregator.PercentileFromHistogram(minute.LatencyHistogram, 50),
                    LatencyP95Ms = RequestMetricsAggregator.PercentileFromHistogram(minute.LatencyHistogram, 95),
                    LatencyP99Ms = RequestMetricsAggregator.PercentileFromHistogram(minute.LatencyHistogram, 99),
                    CpuPercent = minute == latest ? resource.CpuPercent : null,
                    ProcessMemoryMb = minute == latest ? resource.MemoryMb : 0,
                    ThreadCount = minute == latest ? resource.ThreadCount : 0,
                    DiskFreeGb = minute == latest ? resource.DiskFreeGb : 0,
                    DbProbeMs = minute == latest ? resource.DbProbeMs : null,
                    HealthStatus = minute == latest ? resource.HealthStatus : "ok",
                    ProcessUpMinutes = minute == latest ? resource.UpMinutes : 0,
                });

                foreach (var path in minute.Paths)
                {
                    dbContext.EndpointStats.Add(new EndpointStat
                    {
                        MinuteUtc = minute.MinuteUtc,
                        Path = path.Path,
                        Count = path.Count,
                        Errors = path.Errors,
                        TotalMs = path.TotalMs,
                        SlowCount = path.SlowCount,
                    });
                }
            }

            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex))
            {
                // Overlapping minute already persisted — treat as success and continue with the
                // alert pass so the duplicate never suppresses evaluation.
                logger.LogWarning("نمونهٔ متریک تکراری در دقیقهٔ همپوشان نادیده گرفته شد");
                foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State is EntityState.Added))
                {
                    entry.State = EntityState.Detached;
                }
            }

            await DetectBruteForceAsync(now, ct);
            await alertEvaluation.EvaluateAsync(ct);
            await PruneAsync(now, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "اجرای نمونه‌بردار متریک ناموفق بود");
        }
    }

    private sealed record ResourceSnapshot(
        double? CpuPercent, double MemoryMb, int ThreadCount, double DiskFreeGb,
        int? DbProbeMs, string HealthStatus, double UpMinutes);

    /// <summary>Everything measurable from inside a shared-hosting process, honestly labelled as
    /// PROCESS metrics in the UI — the whole machine is not visible from here and pretending
    /// otherwise would make the panel lie.</summary>
    private async Task<ResourceSnapshot> ProbeResourcesAsync(CancellationToken ct)
    {
        var process = Process.GetCurrentProcess();
        var now = timeProvider.GetUtcNow();

        double? cpuPercent = null;
        if (lastCpuReadUtc is { } previousRead)
        {
            var wallSeconds = (now - previousRead).TotalSeconds;
            var cpuSeconds = (process.TotalProcessorTime - lastTotalProcessorTime).TotalSeconds;
            if (wallSeconds > 0)
            {
                cpuPercent = Math.Clamp(cpuSeconds / (wallSeconds * Environment.ProcessorCount) * 100, 0, 100);
            }
        }

        lastCpuReadUtc = now;
        lastTotalProcessorTime = process.TotalProcessorTime;

        var diskFreeGb = new DriveInfo(AppContext.BaseDirectory).AvailableFreeSpace / 1024.0 / 1024 / 1024;

        var (dbProbeMs, healthStatus) = await ProbeDatabaseAsync(ct);

        return new ResourceSnapshot(
            cpuPercent,
            process.WorkingSet64 / 1024.0 / 1024,
            process.Threads.Count,
            diskFreeGb,
            dbProbeMs,
            healthStatus,
            (now.UtcDateTime - process.StartTime.ToUniversalTime()).TotalMinutes);
    }

    /// <summary>Open + SELECT 1 roundtrip on the context's own connection. This is the real health
    /// signal: every feature of the app is downstream of "the database answers at all".</summary>
    private async Task<(int? ProbeMs, string HealthStatus)> ProbeDatabaseAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(ct);
            stopwatch.Stop();

            return (Math.Min((int)stopwatch.ElapsedMilliseconds, int.MaxValue),
                stopwatch.ElapsedMilliseconds > 2000 ? "degraded" : "ok");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "پروب دیتابیس ناموفق بود");
            return (null, "down");
        }
    }

    /// <summary>Brute-force detection: one IP failing logins at volume in the 15-minute window gets
    /// a Critical SuspiciousActivity event — the security dashboard's attack feed, and the input the
    /// owner's failed-login alert rule can page on. One row per IP per window: a detection already
    /// recorded inside the window is not re-recorded, so a sustained attack stays a single line
    /// instead of one per minute.</summary>
    private async Task DetectBruteForceAsync(DateTimeOffset now, CancellationToken ct)
    {
        var windowStart = now - BruteForceWindow;
        var attempts = await dbContext.SecurityEvents.AsNoTracking()
            .Where(e => e.Type == SecurityEventType.FailedLogin &&
                e.IpAddress != null && e.OccurredAt >= windowStart)
            .Select(e => new { e.IpAddress, e.OccurredAt })
            .ToListAsync(ct);

        var flagged = new HashSet<string>();
        foreach (var group in attempts.GroupBy(a => a.IpAddress!)
                     .Where(g => g.Count() >= BruteForceThreshold))
        {
            var ip = group.Key;
            var alreadyFlagged = await dbContext.SecurityEvents.AsNoTracking()
                .AnyAsync(e => e.Type == SecurityEventType.SuspiciousActivity &&
                    e.IpAddress == ip && e.OccurredAt >= windowStart, ct);
            if (alreadyFlagged)
            {
                continue;
            }

            dbContext.SecurityEvents.Add(new SecurityEvent
            {
                Type = SecurityEventType.SuspiciousActivity,
                Severity = SecuritySeverity.Critical,
                OccurredAt = now,
                IpAddress = ip,
                Detail = $"تلاش برای شکستن رمز عبور: {group.Count()} ورود ناموفق از این نشانی در ۱۵ دقیقهٔ اخیر",
            });
            flagged.Add(ip);
        }

        if (flagged.Count > 0)
        {
            await dbContext.SaveChangesAsync(ct);
        }
    }

    private async Task PruneAsync(DateTimeOffset now, CancellationToken ct)
    {
        // IgnoreQueryFilters: these tables are never soft-deleted, so the global IsDeleted filter
        // would only narrow the delete for no reason — and EF refuses ExecuteDelete on filtered
        // queries outright in some versions.
        await dbContext.MetricSamples.IgnoreQueryFilters()
            .Where(s => s.MinuteUtc < now.AddDays(-35)).ExecuteDeleteAsync(ct);
        await dbContext.EndpointStats.IgnoreQueryFilters()
            .Where(s => s.MinuteUtc < now.AddDays(-8)).ExecuteDeleteAsync(ct);
        await dbContext.SecurityEvents.IgnoreQueryFilters()
            .Where(e => e.OccurredAt < now.AddDays(-90)).ExecuteDeleteAsync(ct);
        await dbContext.AlertOccurrences.IgnoreQueryFilters()
            .Where(o => o.Status == AlertStatus.Resolved && o.LastSeenAt < now.AddDays(-90))
            .ExecuteDeleteAsync(ct);
    }

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("unique index", StringComparison.OrdinalIgnoreCase) == true;
}
