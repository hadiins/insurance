using Aqsat.Domain.Enums;
using Aqsat.Domain.Monitoring;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Seed;

/// <summary>
/// Default alert rules for the monitoring dashboard, seeded once on first startup (same pattern as
/// InsuranceLineSeeder: idempotent get-or-create, never HasData — the owner edits these rows from
/// «هشدارها و قوانین» and a migration would trample that). Keyed by (Name, Metric): if the owner
/// deleted a default rule it stays deleted; only a fresh database gets the full set.
/// </summary>
public static class AlertRuleSeeder
{
    public static async Task EnsureSeededAsync(AppDbContext context, CancellationToken ct = default)
    {
        var existing = await context.AlertRules.AsNoTracking()
            .Select(r => new { r.Name, r.Metric })
            .ToListAsync(ct);
        var existingKeys = existing.Select(r => (r.Name, r.Metric)).ToHashSet();

        var defaults = new List<AlertRule>
        {
            new()
            {
                Name = "نرخ خطای سرور",
                Metric = AlertMetric.ErrorRatePercent,
                Comparator = AlertComparator.GreaterThan,
                Threshold = 5,
                WindowMinutes = 5,
                Severity = SecuritySeverity.Critical,
                SmsNotify = true,
            },
            new()
            {
                Name = "تأخیر پاسخ بالا",
                Metric = AlertMetric.LatencyP95Ms,
                Comparator = AlertComparator.GreaterThan,
                Threshold = 3000,
                WindowMinutes = 10,
                Severity = SecuritySeverity.Warning,
            },
            new()
            {
                Name = "ورودهای ناموفق مشکوک",
                Metric = AlertMetric.FailedLoginCount,
                Comparator = AlertComparator.GreaterThan,
                Threshold = 10,
                WindowMinutes = 15,
                Severity = SecuritySeverity.Warning,
            },
            new()
            {
                Name = "از دسترس خارج شدن سرویس",
                Metric = AlertMetric.HealthDown,
                Comparator = AlertComparator.GreaterThan,
                Threshold = 0.5,
                WindowMinutes = 5,
                Severity = SecuritySeverity.Critical,
                SmsNotify = true,
            },
            new()
            {
                Name = "کندی دیتابیس",
                Metric = AlertMetric.DbProbeMs,
                Comparator = AlertComparator.GreaterThan,
                Threshold = 5000,
                WindowMinutes = 10,
                Severity = SecuritySeverity.Warning,
            },
            new()
            {
                Name = "فضای دیسک رو به اتمام",
                Metric = AlertMetric.DiskFreeGb,
                Comparator = AlertComparator.LessThan,
                Threshold = 5,
                WindowMinutes = 15,
                Severity = SecuritySeverity.Critical,
                SmsNotify = true,
            },
        };

        var missing = defaults.Where(d => !existingKeys.Contains((d.Name, d.Metric))).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        context.AlertRules.AddRange(missing);
        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent startups raced the check-then-insert — a duplicate-key failure means the
            // other instance seeded the defaults; detach and move on (rule 24's spirit).
            foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
