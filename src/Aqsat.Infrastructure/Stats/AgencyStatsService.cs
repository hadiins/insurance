using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Stats;

public sealed record AgencyDailyStats(
    DateOnly StatDate,
    int PoliciesIssued,
    int SmsSentCount,
    decimal SmsCostToman,
    int InquiryPaymentsCount,
    decimal InquiryRevenueToman,
    int InquiryCallsCount,
    decimal InquiryCallCostToman);

public sealed record AgencyMonthlyStats(
    int Year,
    int Month,
    int PoliciesIssued,
    int SmsSent,
    int InquiryPayments,
    int InquiryCalls,
    decimal InquiryRevenueToman);

public sealed record AgencyLiveStats(
    int PoliciesIssuedTotal,
    int PoliciesThisMonth,
    int SmsSentTotal,
    decimal SmsCostToman,
    int InquiryPaymentsTotal,
    decimal InquiryRevenueToman,
    int InquiryCallsTotal,
    decimal InquiryCallCostToman,
    IReadOnlyList<AgencyMonthlyStats> MonthlyTrend);

/// <summary>
/// Computes one agency's platform-reporting numbers (پروندهٔ نمایندگی) live from the operational
/// tables. Every read of Policies/ApiIrCallLogs/CustomerPortalInvitations must run inside that
/// agency's RLS scope — the owner's session resolves to HQ, so without the scope switch below
/// every query silently returns zeros (CLAUDE.md rule 17). Shared by AgencyStatsRollupJob (nightly
/// rollup) and the owner's agency-profile endpoint.
/// </summary>
public sealed class AgencyStatsService(AppDbContext dbContext)
{
    private const string SmsServiceSend = "SendSms";
    private const string SmsServiceOtp = "SmsOTP";
    private const string InquiryServiceShahkar = "ShahkarLite";
    private const string InquiryServiceCheque = "ChequeColor";

    /// <summary>Iran abolished DST in 2022 — a fixed +3:30 offset is the correct day boundary.</summary>
    private static readonly TimeSpan IranOffset = TimeSpan.FromHours(3.5);

    public async Task<AgencyLiveStats> GetLiveStatsAsync(Guid agencyId, int trendMonths, CancellationToken ct = default)
    {
        return await RunWithAgencyScopeAsync(agencyId, async () =>
        {
            var policiesTotal = await dbContext.Policies.AsNoTracking().CountAsync(ct);

            var iranToday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(IranOffset).DateTime);
            var monthStart = StartOfJalaliMonth(iranToday);
            var policiesThisMonth = await dbContext.Policies.AsNoTracking()
                .CountAsync(p => p.IssueDate >= monthStart, ct);

            var smsAgg = await dbContext.ApiIrCallLogs.AsNoTracking()
                .Where(l => l.Service == SmsServiceSend || l.Service == SmsServiceOtp)
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Cost = g.Sum(x => x.CostToman) })
                .FirstOrDefaultAsync(ct);

            var inquiryCallAgg = await dbContext.ApiIrCallLogs.AsNoTracking()
                .Where(l => l.Service == InquiryServiceShahkar || l.Service == InquiryServiceCheque)
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Cost = g.Sum(x => x.CostToman) })
                .FirstOrDefaultAsync(ct);

            var paymentAgg = await dbContext.CustomerPortalInvitations.AsNoTracking()
                .Where(i => i.Status == PortalInvitationStatus.Paid && i.PaidAtUtc != null)
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Revenue = g.Sum(x => x.PaidAmountToman ?? 0) })
                .FirstOrDefaultAsync(ct);

            var trend = await ComputeMonthlyTrendAsync(trendMonths, ct);

            return new AgencyLiveStats(
                policiesTotal,
                policiesThisMonth,
                smsAgg?.Count ?? 0,
                smsAgg?.Cost ?? 0,
                paymentAgg?.Count ?? 0,
                paymentAgg?.Revenue ?? 0,
                inquiryCallAgg?.Count ?? 0,
                inquiryCallAgg?.Cost ?? 0,
                trend);
        });
    }

    /// <summary>Day-by-day numbers for [from, to] — the rollup job's input. Zero-activity days are
    /// simply absent from the result (missing = zero everywhere downstream).</summary>
    public async Task<IReadOnlyList<AgencyDailyStats>> ComputeDailyAsync(
        Guid agencyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        return await RunWithAgencyScopeAsync(agencyId, async () =>
        {
            var byDay = new Dictionary<DateOnly, AgencyDailyStats>();
            AgencyDailyStats Bucket(DateOnly day) =>
                byDay.TryGetValue(day, out var existing)
                    ? existing
                    : byDay[day] = new AgencyDailyStats(day, 0, 0, 0, 0, 0, 0, 0);

            var policyCounts = await dbContext.Policies.AsNoTracking()
                .Where(p => p.IssueDate >= from && p.IssueDate <= to)
                .GroupBy(p => p.IssueDate)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            foreach (var row in policyCounts)
            {
                var bucket = Bucket(row.Day);
                byDay[row.Day] = bucket with { PoliciesIssued = row.Count };
            }

            // The ±1-day margin covers the 3:30 offset: a UTC timestamp near midnight can land on
            // the adjacent Iranian day.
            var utcFrom = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), IranOffset).ToUniversalTime().AddDays(-1);
            var utcTo = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), IranOffset).ToUniversalTime().AddDays(1);

            var callRows = await dbContext.ApiIrCallLogs.AsNoTracking()
                .Where(l => l.CalledAt >= utcFrom && l.CalledAt <= utcTo)
                .Select(l => new { l.Service, l.CalledAt, l.CostToman })
                .ToListAsync(ct);
            foreach (var row in callRows)
            {
                var day = DateOnly.FromDateTime(row.CalledAt.ToOffset(IranOffset).DateTime);
                if (day < from || day > to)
                {
                    continue;
                }

                var bucket = Bucket(day);
                if (row.Service == SmsServiceSend || row.Service == SmsServiceOtp)
                {
                    byDay[day] = bucket with
                    {
                        SmsSentCount = bucket.SmsSentCount + 1,
                        SmsCostToman = bucket.SmsCostToman + row.CostToman,
                    };
                }
                else if (row.Service == InquiryServiceShahkar || row.Service == InquiryServiceCheque)
                {
                    byDay[day] = bucket with
                    {
                        InquiryCallsCount = bucket.InquiryCallsCount + 1,
                        InquiryCallCostToman = bucket.InquiryCallCostToman + row.CostToman,
                    };
                }
            }

            var paymentRows = await dbContext.CustomerPortalInvitations.AsNoTracking()
                .Where(i => i.Status == PortalInvitationStatus.Paid
                    && i.PaidAtUtc != null && i.PaidAtUtc >= utcFrom && i.PaidAtUtc <= utcTo)
                .Select(i => new { PaidAt = i.PaidAtUtc!.Value, Amount = i.PaidAmountToman ?? 0 })
                .ToListAsync(ct);
            foreach (var row in paymentRows)
            {
                var day = DateOnly.FromDateTime(row.PaidAt.ToOffset(IranOffset).DateTime);
                if (day < from || day > to)
                {
                    continue;
                }

                var bucket = Bucket(day);
                byDay[day] = bucket with
                {
                    InquiryPaymentsCount = bucket.InquiryPaymentsCount + 1,
                    InquiryRevenueToman = bucket.InquiryRevenueToman + row.Amount,
                };
            }

            return byDay.Values.OrderBy(d => d.StatDate).ToList();
        });
    }

    /// <summary>Earliest day any of this agency's counted activity happened — the start bound of a
    /// full rebuild. null means the agency has no activity at all.</summary>
    public async Task<DateOnly?> GetEarliestActivityAsync(Guid agencyId, CancellationToken ct = default)
    {
        return await RunWithAgencyScopeAsync<DateOnly?>(agencyId, async () =>
        {
            var candidates = new List<DateOnly>();

            var firstPolicy = await dbContext.Policies.AsNoTracking()
                .Where(p => p.IssueDate != default)
                .OrderBy(p => p.IssueDate)
                .Select(p => (DateOnly?)p.IssueDate)
                .FirstOrDefaultAsync(ct);
            if (firstPolicy is { } policyDay)
            {
                candidates.Add(policyDay);
            }

            var firstCall = await dbContext.ApiIrCallLogs.AsNoTracking()
                .OrderBy(l => l.CalledAt)
                .Select(l => (DateTimeOffset?)l.CalledAt)
                .FirstOrDefaultAsync(ct);
            if (firstCall is { } callAt)
            {
                candidates.Add(DateOnly.FromDateTime(callAt.ToOffset(IranOffset).DateTime));
            }

            var firstPayment = await dbContext.CustomerPortalInvitations.AsNoTracking()
                .Where(i => i.Status == PortalInvitationStatus.Paid && i.PaidAtUtc != null)
                .OrderBy(i => i.PaidAtUtc)
                .Select(i => i.PaidAtUtc!.Value)
                .FirstOrDefaultAsync(ct);
            if (firstPayment != default)
            {
                candidates.Add(DateOnly.FromDateTime(firstPayment.ToOffset(IranOffset).DateTime));
            }

            return candidates.Count == 0 ? null : candidates.Min();
        });
    }

    /// <summary>Trend bucketed by Jalali months — Persian users read روند ماهانه in فروردین…اسفند,
    /// and the Year/Month in each row is a Jalali year/month number.</summary>
    private async Task<IReadOnlyList<AgencyMonthlyStats>> ComputeMonthlyTrendAsync(int months, CancellationToken ct)
    {
        var iranToday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(IranOffset).DateTime);
        var currentKey = JalaliDate.MonthKey(iranToday);
        var firstKey = currentKey - (months - 1);

        // A 400-day window always contains the oldest of the 12 target Jalali months with slack;
        // events outside the target months are dropped after the Jalali mapping, not by this bound.
        var windowStart = iranToday.AddDays(-400);
        var utcStart = new DateTimeOffset(windowStart.ToDateTime(TimeOnly.MinValue), IranOffset).ToUniversalTime();

        var targetKeys = Enumerable.Range(0, months).Select(i => firstKey + i).ToHashSet();

        var policyByDay = await dbContext.Policies.AsNoTracking()
            .Where(p => p.IssueDate >= windowStart)
            .GroupBy(p => p.IssueDate)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var callRows = await dbContext.ApiIrCallLogs.AsNoTracking()
            .Where(l => l.CalledAt >= utcStart)
            .Select(l => new { l.Service, l.CalledAt })
            .ToListAsync(ct);

        var paymentRows = await dbContext.CustomerPortalInvitations.AsNoTracking()
            .Where(i => i.Status == PortalInvitationStatus.Paid && i.PaidAtUtc != null && i.PaidAtUtc >= utcStart)
            .Select(i => new { PaidAt = i.PaidAtUtc!.Value, Amount = i.PaidAmountToman ?? 0 })
            .ToListAsync(ct);

        var buckets = targetKeys.ToDictionary(k => k, _ => (Policies: 0, Sms: 0, Payments: 0, Revenue: 0m, InquiryCalls: 0));

        foreach (var row in policyByDay)
        {
            var key = JalaliDate.MonthKey(row.Day);
            if (buckets.TryGetValue(key, out var bucket))
            {
                buckets[key] = (bucket.Policies + row.Count, bucket.Sms, bucket.Payments, bucket.Revenue, bucket.InquiryCalls);
            }
        }

        foreach (var row in callRows)
        {
            var key = JalaliDate.MonthKey(DateOnly.FromDateTime(row.CalledAt.ToOffset(IranOffset).DateTime));
            if (!buckets.TryGetValue(key, out var bucket))
            {
                continue;
            }

            if (row.Service == SmsServiceSend || row.Service == SmsServiceOtp)
            {
                buckets[key] = (bucket.Policies, bucket.Sms + 1, bucket.Payments, bucket.Revenue, bucket.InquiryCalls);
            }
            else if (row.Service == InquiryServiceShahkar || row.Service == InquiryServiceCheque)
            {
                buckets[key] = (bucket.Policies, bucket.Sms, bucket.Payments, bucket.Revenue, bucket.InquiryCalls + 1);
            }
        }

        foreach (var row in paymentRows)
        {
            var key = JalaliDate.MonthKey(DateOnly.FromDateTime(row.PaidAt.ToOffset(IranOffset).DateTime));
            if (buckets.TryGetValue(key, out var bucket))
            {
                buckets[key] = (bucket.Policies, bucket.Sms, bucket.Payments + 1, bucket.Revenue + row.Amount, bucket.InquiryCalls);
            }
        }

        return targetKeys.OrderBy(k => k)
            .Select(k =>
            {
                var b = buckets[k];
                return new AgencyMonthlyStats(k / 12, k % 12 + 1, b.Policies, b.Sms, b.Payments, b.InquiryCalls, b.Revenue);
            })
            .ToList();
    }

    /// <summary>Walks back at most 31 days to the first day of the current Jalali month — the SQL
    /// bound for "policies issued this month".</summary>
    private static DateOnly StartOfJalaliMonth(DateOnly today)
    {
        var key = JalaliDate.MonthKey(today);
        var probe = today;
        while (probe > today.AddDays(-40) && JalaliDate.MonthKey(probe.AddDays(-1)) == key)
        {
            probe = probe.AddDays(-1);
        }

        return probe;
    }

    /// <summary>PortalInvitationService.RunWithAgencyScopeAsync pattern: swap the ambient agency,
    /// reopen the connection so the session-context interceptor re-stamps it, run every read
    /// RLS-scoped to the target agency, then restore the caller's scope.</summary>
    private async Task<TResult> RunWithAgencyScopeAsync<TResult>(Guid agencyId, Func<Task<TResult>> work)
    {
        var previousAgency = AgencyContext.Current;
        AgencyContext.Current = agencyId;
        try
        {
            await dbContext.Database.OpenConnectionAsync();
            try
            {
                return await work();
            }
            finally
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            AgencyContext.Current = previousAgency;
        }
    }
}
