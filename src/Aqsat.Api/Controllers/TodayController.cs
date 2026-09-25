using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Today;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Concurrency;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// The Today dashboard's figures in one call — collected today and its 7-day baseline, what falls
/// due today, what is more than 30 days overdue, cheques in flight, the 14-day collection trend
/// against the agency's monthly goal, and the activity feed.
///
/// Deliberately arithmetic over rows the system already holds (nothing here is a new ledger), and
/// deliberately silent about what the data cannot support: there is no historical balance snapshot,
/// so the overdue figure carries no change-percentage, and no bounce timestamp, so the bounced-cheque
/// count is not narrowed to a 30-day window. A fabricated delta would be worse than none.
///
/// The worklist itself is not served here — the Today page uses the existing
/// <c>GET /api/installments</c> surface, which already filters and paginates.
/// </summary>
[ApiController]
[Route("api/today")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class TodayController(
    AppDbContext dbContext,
    ICurrentUserContext currentUser,
    PresenceConnectionRegistry connectionRegistry,
    TimeProvider timeProvider) : ControllerBase
{
    /// <summary>Iran abolished DST in 2022, so a fixed +3:30 offset is the correct day boundary for
    /// bucketing instants (ReminderLog.SentAt) into an Iranian civil day — same convention
    /// AgencyStatsService uses.</summary>
    private static readonly TimeSpan IranOffset = TimeSpan.FromHours(3.5);

    private const int TrendDays = 14;
    private const int SparklineDays = 7;
    private const int OverdueThresholdDays = 30;

    [HttpGet]
    public async Task<ActionResult<TodayDashboardDto>> Get(CancellationToken ct)
    {
        // Civil dates (DueDate, SettlementDeadline, PaidOn) are supplied by agents, not derived from
        // instants — so they are compared against a civil "today", the same one CountdownController
        // uses, keeping the countdown panel and this page reading the same day.
        var today = IranClock.Today(timeProvider);
        var nowUtc = timeProvider.GetUtcNow();

        var figures = await BuildFiguresAsync(today, ct);
        var goal = await BuildGoalAsync(today, ct);
        var trend = await BuildTrendAsync(today, goal.DailyTarget, ct);
        var activity = await BuildActivityAsync(today, ct);
        var sync = await BuildSyncAsync(nowUtc, ct);

        return Ok(new TodayDashboardDto(
            figures,
            trend,
            goal,
            activity,
            sync,
            connectionRegistry.DistinctAppUserCount(currentUser.ActiveOrganizationId)));
    }

    private async Task<TodayFiguresDto> BuildFiguresAsync(DateOnly today, CancellationToken ct)
    {
        // One query covers both the 7-bar sparkline (today-6 … today) and the baseline it is
        // compared against (today-7 … today-1).
        var windowStart = today.AddDays(-SparklineDays);
        var paymentRows = await dbContext.Payments.AsNoTracking()
            .Where(p => p.PaidOn >= windowStart && p.PaidOn <= today)
            .GroupBy(p => p.PaidOn)
            .Select(g => new { Day = g.Key, Total = g.Sum(p => p.Amount) })
            .ToListAsync(ct);
        var collectedByDay = paymentRows.ToDictionary(r => r.Day, r => r.Total);

        decimal CollectedOn(DateOnly day) => collectedByDay.TryGetValue(day, out var total) ? total : 0m;

        var collectedToday = CollectedOn(today);
        var previousSevenDayTotal = Enumerable.Range(1, SparklineDays).Sum(offset => CollectedOn(today.AddDays(-offset)));
        var sparkline = Enumerable.Range(0, SparklineDays).Select(offset => CollectedOn(today.AddDays(-(SparklineDays - 1) + offset))).ToList();

        var dueToday = await dbContext.Installments.AsNoTracking()
            .Where(i => i.DueDate == today)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Settled = g.Count(i => i.Status == InstallmentStatus.Settled),
            })
            .FirstOrDefaultAsync(ct);

        var overdueCutoff = today.AddDays(-OverdueThresholdDays);
        var overdue = await dbContext.Installments.AsNoTracking()
            .Where(i => i.Status != InstallmentStatus.Settled && i.DueDate < overdueCutoff)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Amount = g.Sum(i => i.Amount - i.PaidAmount) })
            .FirstOrDefaultAsync(ct);

        var chequesInFlight = await dbContext.PaymentCheques.AsNoTracking()
            .CountAsync(c => c.Status == CollateralStatus.Held || c.Status == CollateralStatus.AtBank, ct);
        var chequesBounced = await dbContext.PaymentCheques.AsNoTracking()
            .CountAsync(c => c.Status == CollateralStatus.Bounced, ct);

        var dueTodayTotal = dueToday?.Total ?? 0;
        var dueTodaySettled = dueToday?.Settled ?? 0;

        return new TodayFiguresDto(
            collectedToday,
            TodayMath.DeltaPercent(collectedToday, previousSevenDayTotal),
            sparkline,
            dueTodayTotal,
            dueTodayTotal - dueTodaySettled,
            dueTodaySettled,
            overdue?.Amount ?? 0m,
            overdue?.Count ?? 0,
            chequesInFlight,
            chequesBounced);
    }

    private async Task<IReadOnlyList<TodayTrendPointDto>> BuildTrendAsync(
        DateOnly today, decimal? dailyTarget, CancellationToken ct)
    {
        var trendStart = today.AddDays(-(TrendDays - 1));
        var rows = await dbContext.Payments.AsNoTracking()
            .Where(p => p.PaidOn >= trendStart && p.PaidOn <= today)
            .GroupBy(p => p.PaidOn)
            .Select(g => new { Day = g.Key, Total = g.Sum(p => p.Amount) })
            .ToListAsync(ct);
        var collectedByDay = rows.ToDictionary(r => r.Day, r => r.Total);

        return Enumerable.Range(0, TrendDays)
            .Select(offset =>
            {
                var day = trendStart.AddDays(offset);
                var collected = collectedByDay.TryGetValue(day, out var total) ? total : 0m;
                return new TodayTrendPointDto(day, collected, TodayMath.GoalRemaining(collected, dailyTarget));
            })
            .ToList();
    }

    private async Task<TodayGoalDto> BuildGoalAsync(DateOnly today, CancellationToken ct)
    {
        var monthlyGoal = await MonthlyGoalAsync(ct);
        var monthStart = TodayMath.StartOfJalaliMonth(today);

        var monthCollected = await dbContext.Payments.AsNoTracking()
            .Where(p => p.PaidOn >= monthStart && p.PaidOn <= today)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        return new TodayGoalDto(
            monthlyGoal,
            TodayMath.DailyTarget(monthlyGoal, today),
            monthCollected,
            TodayMath.MonthProgressPercent(monthCollected, monthlyGoal));
    }

    private async Task<decimal?> MonthlyGoalAsync(CancellationToken ct)
    {
        var settings = await dbContext.OrgSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == currentUser.ActiveOrganizationId, ct);
        return settings?.MonthlyCollectionGoal;
    }

    /// <summary>Every row here traces to a stored record and says only what that record supports:
    /// a failed reminder is «ناموفق», not «شماره نامعتبر» — <see cref="ReminderSendStatus"/> has no
    /// invalid-number state to report.</summary>
    private async Task<IReadOnlyList<TodayActivityDto>> BuildActivityAsync(DateOnly today, CancellationToken ct)
    {
        var dayStartUtc = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), IranOffset).ToUniversalTime();
        var dayEndUtc = dayStartUtc.AddDays(1);

        var remindersSent = await dbContext.ReminderLogs.AsNoTracking()
            .CountAsync(r => r.SentAt >= dayStartUtc && r.SentAt < dayEndUtc && r.Status == ReminderSendStatus.Sent, ct);
        var remindersFailed = await dbContext.ReminderLogs.AsNoTracking()
            .CountAsync(r => r.SentAt >= dayStartUtc && r.SentAt < dayEndUtc && r.Status == ReminderSendStatus.Failed, ct);
        var receiptsToday = await dbContext.Payments.AsNoTracking()
            .CountAsync(p => p.PaidOn == today, ct);

        // A bounced cheque's Payment is soft-deleted by the very reversal the bounce performs, and
        // the global soft-delete filter drops the row through the required Payment join — so this
        // feed would sit empty in exactly the case it exists to report. Same fix
        // PaymentChequesController applies: drop the filter, then re-apply the root's own
        // soft-delete and the agency scope by hand (RLS is DB-side and unaffected).
        var bounced = await dbContext.PaymentCheques.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && c.AgencyId == currentUser.ActiveOrganizationId)
            .Where(c => c.Status == CollateralStatus.Bounced)
            .OrderByDescending(c => c.Payment.PaidOn)
            .Take(3)
            .Select(c => new { c.ChequeNumber, CustomerName = c.Payment.Customer.FullName })
            .ToListAsync(ct);

        var rows = new List<TodayActivityDto>();
        if (remindersSent > 0)
        {
            rows.Add(new TodayActivityDto("sms", "ok", $"{remindersSent} پیامک سرسید", "امروز ارسال شد"));
        }

        if (remindersFailed > 0)
        {
            rows.Add(new TodayActivityDto("sms-failed", "warn", $"{remindersFailed} پیامک ناموفق", "امروز — نیاز به بررسی دستی"));
        }

        foreach (var cheque in bounced)
        {
            rows.Add(new TodayActivityDto("cheque-bounced", "bad", $"چک {cheque.ChequeNumber} برگشت خورد", cheque.CustomerName));
        }

        if (receiptsToday > 0)
        {
            rows.Add(new TodayActivityDto("receipt", "ok", $"{receiptsToday} رسید پرداخت", "امروز ثبت شد"));
        }

        return rows;
    }

    private async Task<TodaySyncDto> BuildSyncAsync(DateTimeOffset nowUtc, CancellationToken ct)
    {
        var lastSyncAt = await dbContext.ImportBatches.AsNoTracking()
            .Where(b => b.CreatedAtUtc != null)
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => b.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var openMismatches = await dbContext.ImportRows.AsNoTracking()
            .CountAsync(r => r.Status == ImportRowStatus.Failed, ct);

        var agoMinutes = lastSyncAt is null
            ? null
            : (int?)Math.Max(0, (int)(nowUtc - lastSyncAt.Value).TotalMinutes);

        return new TodaySyncDto(lastSyncAt, agoMinutes, openMismatches);
    }
}
