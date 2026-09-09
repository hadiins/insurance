using Aqsat.Application.Auth;
using Aqsat.Application.Sms;
using Aqsat.Domain.Enums;
using Aqsat.Domain.Monitoring;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aqsat.Infrastructure.Monitoring;

/// <summary>
/// The alert engine MetricsSamplerJob invokes after persisting each minute's samples: loads the
/// owner's rules, builds the evaluation window from MetricSample/SecurityEvent rows, and moves
/// AlertOccurrences through their lifecycle (Active → Acknowledged → Resolved). Pure threshold
/// math lives in AlertEvaluator; this service owns the I/O and the state transitions.
///
/// SMS discipline (rule 25's cost logic applied to owner paging): sent ONLY when a rule first
/// fires (a brand-new Active occurrence), only for rules with SmsNotify, and at most once per
/// 30 minutes per rule (LastNotifiedAt) — a flapping threshold must never drain the SMS balance.
/// </summary>
public sealed class AlertEvaluationService(
    AppDbContext dbContext,
    ISmsSender smsSender,
    TimeProvider timeProvider,
    ILogger<AlertEvaluationService> logger)
{
    private static readonly TimeSpan SmsCooldown = TimeSpan.FromMinutes(30);

    public async Task EvaluateAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        var rules = await dbContext.AlertRules
            .Where(r => !r.IsDeleted && r.IsEnabled)
            .ToListAsync(ct);
        if (rules.Count == 0)
        {
            return;
        }

        // One fetch for the widest window any rule needs; each rule then slices its own tail.
        var windowStart = now.AddMinutes(-Math.Max(rules.Max(r => r.WindowMinutes), 1));
        var samples = await dbContext.MetricSamples.AsNoTracking()
            .Where(s => s.MinuteUtc >= windowStart)
            .ToListAsync(ct);
        var failedLogins = await dbContext.SecurityEvents.AsNoTracking()
            .Where(e => e.Type == SecurityEventType.FailedLogin && e.OccurredAt >= windowStart)
            .ToListAsync(ct);

        var openOccurrences = await dbContext.AlertOccurrences
            .Where(o => o.Status != AlertStatus.Resolved)
            .ToDictionaryAsync(o => o.RuleId, ct);

        // A disabled (or deleted) rule must not leave its last breach hanging as Active forever —
        // resolve it here so the board only ever shows live rules.
        foreach (var orphaned in openOccurrences.Values.Where(o => rules.All(r => r.Id != o.RuleId)))
        {
            orphaned.Status = AlertStatus.Resolved;
            orphaned.LastSeenAt = now;
        }

        foreach (var rule in rules)
        {
            ct.ThrowIfCancellationRequested();

            var cutoff = now.AddMinutes(-Math.Max(rule.WindowMinutes, 1));
            var windowSamples = samples.Where(s => s.MinuteUtc >= cutoff).ToList();
            var requests = windowSamples.Sum(s => s.Requests);
            var snapshot = new AlertWindowSnapshot(
                requests == 0 ? null : windowSamples.Sum(s => s.Errors) * 100.0 / requests,
                windowSamples.Select(s => (double?)s.LatencyP95Ms).DefaultIfEmpty().Max(),
                windowSamples.Select(s => s.CpuPercent).DefaultIfEmpty().Max(),
                windowSamples.Select(s => (double?)s.ProcessMemoryMb).DefaultIfEmpty().Max(),
                windowSamples.Where(s => s.DbProbeMs.HasValue).Select(s => (double?)s.DbProbeMs!.Value).Max(),
                windowSamples.Select(s => (double?)s.DiskFreeGb).DefaultIfEmpty().Min(),
                failedLogins.Count(e => e.OccurredAt >= cutoff),
                windowSamples.Any(s => s.HealthStatus == "down"));

            var observed = AlertEvaluator.Observe(rule.Metric, snapshot);
            var breached = observed is { } value && AlertEvaluator.IsBreached(rule.Comparator, rule.Threshold, value);

            if (!breached)
            {
                // Condition cleared on its own — the sampler resolves so the owner wakes up to a
                // clean board, not yesterday's noise. Acknowledged occurrences resolve too.
                if (openOccurrences.TryGetValue(rule.Id, out var stale))
                {
                    stale.Status = AlertStatus.Resolved;
                    stale.LastSeenAt = now;
                }

                continue;
            }

            var message = AlertEvaluator.ComposeMessage(rule.Name, rule.Metric, rule.Comparator, rule.Threshold, observed!.Value);

            if (openOccurrences.TryGetValue(rule.Id, out var occurrence))
            {
                // Already firing: extend, but keep an Acknowledged occurrence acknowledged — the
                // owner has seen it; re-escalating would only re-page them for the same incident.
                occurrence.LastSeenAt = now;
                occurrence.ObservedValue = observed!.Value;
                occurrence.Message = message;
                continue;
            }

            dbContext.AlertOccurrences.Add(new AlertOccurrence
            {
                RuleId = rule.Id,
                Status = AlertStatus.Active,
                StartedAt = now,
                LastSeenAt = now,
                ObservedValue = observed!.Value,
                Message = message,
            });

            if (rule.SmsNotify && (rule.LastNotifiedAt is null || now - rule.LastNotifiedAt >= SmsCooldown))
            {
                await NotifyOwnersAsync(rule, message, ct);
                rule.LastNotifiedAt = now;
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }

    /// <summary>Every active user holding the Platform.Owner permission — the same audience the
    /// endpoints target. Each send is charged, so a failure to reach one owner never blocks the
    /// rest; failures are logged, never thrown (an unpageable alert must not kill the sampler).</summary>
    private async Task NotifyOwnersAsync(AlertRule rule, string message, CancellationToken ct)
    {
        var mobiles = await dbContext.UserOrgRoles.AsNoTracking()
            .Where(m => !m.IsDeleted &&
                m.Role.RolePermissions.Any(p => !p.IsDeleted && p.Permission == Permissions.PlatformOwner))
            .Join(dbContext.Users.Where(u => !u.IsDeleted && u.IsActive),
                m => m.UserId, u => u.Id, (m, u) => u.Mobile)
            .Distinct()
            .ToListAsync(ct);

        foreach (var mobile in mobiles)
        {
            try
            {
                // Guid.Empty agencyId = the platform's own SMS account (same convention as the
                // signup OTP flow), not any agency's balance.
                var sent = await smsSender.SendAsync(mobile, $"Credix — {message}", Guid.Empty, ct);
                if (!sent)
                {
                    logger.LogWarning("پیامک هشدار «{RuleName}» به {Mobile} ارسال نشد", rule.Name, mobile);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "خطا در ارسال پیامک هشدار «{RuleName}» به {Mobile}", rule.Name, mobile);
            }
        }
    }
}
