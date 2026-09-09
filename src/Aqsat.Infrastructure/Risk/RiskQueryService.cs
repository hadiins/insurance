using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Risk;

/// <summary>One point of a per-day trend series (docs Phase 2A §6 charts) — the Jalali label is
/// composed at render time, the bucket is the UTC date.</summary>
public sealed record TrendPointDto(DateTimeOffset Date, decimal Value);

/// <summary>GET /api/risk/dashboard — every KPI and chart series of the risk dashboard (docs Phase
/// 2A §6), computed from live installment/payment data plus the persisted assessment snapshots.
/// All reads run in the caller's RLS scope.</summary>
public sealed record RiskDashboardDto(
    int TotalCustomers,
    int AssessedCustomers,
    int VeryLowCount,
    int LowCount,
    int MediumCount,
    int HighCount,
    int CriticalCount,
    decimal CurrentDebtToman,
    decimal TotalOverdueToman,
    decimal? OnTimeRatePercent,
    decimal DefaultRatePercent,
    int OpenReviewsCount,
    int UnreadWarningsCount,
    IReadOnlyList<TrendPointDto> ScoreTrend,
    IReadOnlyList<TrendPointDto> OverdueTrend,
    IReadOnlyList<TrendPointDto> OnTimeTrend,
    IReadOnlyList<TrendPointDto> HighRiskTrend,
    IReadOnlyList<WarningTypeCountDto> WarningsByType);

public sealed record WarningTypeCountDto(string Type, string TypeFa, int Count);

/// <summary>GET /api/risk/high-risk — the operational high-risk list (overdue installments,
/// bounced cheques) merged with each customer's latest risk assessment, so the page shows WHY the
/// customer is risky, not just THAT they are (docs Phase 2A §6/§20).</summary>
public sealed record HighRiskRiskCustomerDto(
    Guid CustomerId,
    string FullName,
    string? Mobile,
    int OverdueInstallmentCount,
    int MaxDaysOverdue,
    int BouncedChequeCount,
    decimal OverdueAmountToman,
    int? Score,
    RiskLevel? RiskLevel,
    RiskDecision? Decision);

public sealed record RiskWarningItemDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    RiskWarningType Type,
    string Message,
    bool IsRead,
    DateTimeOffset CreatedAt);

public sealed record ManualReviewListItemDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    Guid AssessmentId,
    int Score,
    RiskLevel RiskLevel,
    RiskDecision RecommendedDecision,
    RiskDecision? FinalDecision,
    ManualReviewStatus Status,
    string? AssignedToName,
    decimal CurrentDebtToman,
    decimal OverdueAmountToman,
    int OverdueCount,
    int ReturnedChequeCount,
    string? TriggeredRulesJson,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

/// <summary>
/// The read side of the risk feature (docs Phase 2A §6/§15/§19/§20): dashboard aggregation, the
/// merged high-risk view, the warnings feed and the manual-review queue. Every query is set-based
/// over the existing tables inside the caller's RLS scope — no duplicated reporting model.
/// </summary>
public sealed class RiskQueryService(AppDbContext dbContext, TimeProvider timeProvider)
{
    private const int TrendWindowDays = 180;

    public async Task<RiskDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var agencyId = AgencyContext.Current ?? throw new RiskAssessmentException("دامنهٔ نمایندگی نامعتبر است.");
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var windowStart = DateTimeOffset.UtcNow.AddDays(-TrendWindowDays);

        var writeOffDays = await dbContext.OrgSettings.AsNoTracking()
            .Where(o => o.OrganizationId == agencyId)
            .Select(o => o.DefaultWriteOffDays)
            .FirstOrDefaultAsync(ct);
        if (writeOffDays == 0)
        {
            writeOffDays = 30;
        }

        // ---- Live installment position (same definitions as RiskFeatureCalculator) ----
        var openInstallments = await dbContext.Installments.AsNoTracking()
            .Where(i => i.Status != InstallmentStatus.Settled)
            .Select(i => new { i.Amount, i.PaidAmount, i.DueDate, i.Policy.CustomerId })
            .ToListAsync(ct);

        var currentDebt = openInstallments.Sum(i => i.Amount - i.PaidAmount);
        var overdue = openInstallments.Where(i => i.DueDate < today).ToList();
        var totalOverdue = overdue.Sum(i => i.Amount - i.PaidAmount);
        var defaultCustomers = overdue
            .Where(i => today.DayNumber - i.DueDate.DayNumber > writeOffDays)
            .Select(i => i.CustomerId)
            .Distinct()
            .Count();
        var customersWithInstallments = openInstallments.Select(i => i.CustomerId).Distinct().Count();

        // ---- Agency-wide on-time rate: settled installments whose LAST payment landed by the due
        // date — the exact rule the per-customer calculator applies (rule 21). ----
        var settled = await dbContext.Installments.AsNoTracking()
            .Where(i => i.Status == InstallmentStatus.Settled)
            .Select(i => new { i.Id, i.DueDate })
            .ToListAsync(ct);
        decimal? onTimeRate = null;
        if (settled.Count > 0)
        {
            var settledIds = settled.Select(i => i.Id).ToList();
            var lastPaidOn = await dbContext.PaymentAllocations.AsNoTracking()
                .Where(a => settledIds.Contains(a.InstallmentId))
                .GroupBy(a => a.InstallmentId)
                .Select(g => new { InstallmentId = g.Key, LastPaidOn = g.Max(a => a.Payment.PaidOn) })
                .ToDictionaryAsync(x => x.InstallmentId, x => x.LastPaidOn, ct);
            var onTime = settled.Count(i => lastPaidOn.TryGetValue(i.Id, out var paidOn) && paidOn <= i.DueDate);
            onTimeRate = 100m * onTime / settled.Count;
        }

        // ---- Latest assessment per customer (level distribution + assessed count) ----
        var latestIds = await dbContext.RiskAssessments.AsNoTracking()
            .GroupBy(a => a.CustomerId)
            .Select(g => g.OrderByDescending(a => a.CalculatedAt).First().Id)
            .ToListAsync(ct);
        var latest = await dbContext.RiskAssessments.AsNoTracking()
            .Where(a => latestIds.Contains(a.Id))
            .Select(a => new { a.RiskLevel })
            .ToListAsync(ct);
        int Count(RiskLevel level) => latest.Count(a => a.RiskLevel == level);

        // ---- Trends from assessment snapshots inside the window ----
        var windowAssessments = await dbContext.RiskAssessments.AsNoTracking()
            .Where(a => a.CalculatedAt >= windowStart)
            .Select(a => new
            {
                a.Score, a.RiskLevel, a.OverdueAmountToman, a.OnTimeRatePercent,
                a.SettledInstallmentCount, a.CalculatedAt,
            })
            .ToListAsync(ct);
        var scoreTrend = BucketByDay(
            windowAssessments, a => DateOnly.FromDateTime(a.CalculatedAt.UtcDateTime), a => a.Score);
        var overdueTrend = BucketByDay(
            windowAssessments, a => DateOnly.FromDateTime(a.CalculatedAt.UtcDateTime), a => a.OverdueAmountToman);
        // Only assessments with settled installments carry a real on-time rate — the 0 stored for
        // "no history yet" would fake a plunge in the agency average.
        var onTimeTrend = BucketByDay(
            windowAssessments.Where(a => a.SettledInstallmentCount > 0).ToList(),
            a => DateOnly.FromDateTime(a.CalculatedAt.UtcDateTime), a => a.OnTimeRatePercent);
        var highRiskTrend = windowAssessments
            .Where(a => a.RiskLevel >= RiskLevel.High)
            .GroupBy(a => DateOnly.FromDateTime(a.CalculatedAt.UtcDateTime))
            .Select(g => new TrendPointDto(new DateTimeOffset(g.Key.ToDateTime(TimeOnly.MinValue)), g.Count()))
            .OrderBy(p => p.Date)
            .ToList();

        // ---- Warnings and review queue ----
        var warningsByType = await dbContext.RiskWarnings.AsNoTracking()
            .GroupBy(w => w.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var openReviews = await dbContext.ManualReviews.AsNoTracking()
            .CountAsync(r => r.Status == ManualReviewStatus.Pending
                || r.Status == ManualReviewStatus.InReview
                || r.Status == ManualReviewStatus.RequestMoreInfo, ct);
        var unreadWarnings = await dbContext.RiskWarnings.AsNoTracking()
            .CountAsync(w => !w.IsRead, ct);

        return new RiskDashboardDto(
            await dbContext.Customers.AsNoTracking().CountAsync(ct),
            latest.Count,
            Count(RiskLevel.VeryLow), Count(RiskLevel.Low), Count(RiskLevel.Medium),
            Count(RiskLevel.High), Count(RiskLevel.Critical),
            currentDebt,
            totalOverdue,
            onTimeRate,
            customersWithInstallments == 0 ? 0 : 100m * defaultCustomers / customersWithInstallments,
            openReviews,
            unreadWarnings,
            scoreTrend,
            overdueTrend,
            onTimeTrend,
            highRiskTrend,
            warningsByType
                .Select(w => new WarningTypeCountDto(w.Type.ToString(), WarningTypeFa(w.Type), w.Count))
                .OrderByDescending(w => w.Count)
                .ToList());
    }

    /// <summary>DOC §6 chart series bucketing — the daily average of a snapshot measure. Averaging
    /// (not summing) keeps the series stable when the nightly job assesses many customers per day.</summary>
    private static List<TrendPointDto> BucketByDay<T>(
        List<T> rows, Func<T, DateOnly> date, Func<T, decimal> value) =>
        rows
            .GroupBy(date)
            .Select(g => new TrendPointDto(
                new DateTimeOffset(g.Key.ToDateTime(TimeOnly.MinValue)),
                Math.Round(g.Average(value), MidpointRounding.AwayFromZero)))
            .OrderBy(p => p.Date)
            .ToList();

    public async Task<IReadOnlyList<HighRiskRiskCustomerDto>> GetHighRiskAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var overdueByCustomer = await dbContext.Installments.AsNoTracking()
            .Where(i => i.Status != InstallmentStatus.Settled && i.DueDate < today)
            .Select(i => new { i.Policy.CustomerId, Remaining = i.Amount - i.PaidAmount, i.DueDate })
            .ToListAsync(ct);

        var bouncedByCustomer = await dbContext.Collaterals.AsNoTracking()
            .Where(c => c.Status == CollateralStatus.Bounced)
            .Select(c => c.Policy.CustomerId)
            .ToListAsync(ct);
        var bouncedPaymentCheques = await dbContext.PaymentCheques.AsNoTracking()
            .Where(q => q.Status == CollateralStatus.Bounced)
            .Select(q => q.Payment.CustomerId)
            .ToListAsync(ct);

        var operational = overdueByCustomer
            .GroupBy(x => x.CustomerId)
            .ToDictionary(
                g => g.Key,
                g => (Count: g.Count(),
                      MaxDays: g.Max(x => today.DayNumber - x.DueDate.DayNumber),
                      Amount: g.Sum(x => x.Remaining)));
        var bounced = bouncedByCustomer.Concat(bouncedPaymentCheques)
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        // Scored customers at High/Critical are risky even before anything is overdue today —
        // the merged view unions both sets (docs Phase 2A §6 "تعداد مشتریان واردشده به High Risk").
        var latestIds = await dbContext.RiskAssessments.AsNoTracking()
            .GroupBy(a => a.CustomerId)
            .Select(g => g.OrderByDescending(a => a.CalculatedAt).First().Id)
            .ToListAsync(ct);
        var latestAssessments = await dbContext.RiskAssessments.AsNoTracking()
            .Where(a => latestIds.Contains(a.Id))
            .Select(a => new { a.CustomerId, a.Score, a.RiskLevel, a.Decision })
            .ToListAsync(ct);

        var customerIds = operational.Keys
            .Union(bounced.Keys)
            .Union(latestAssessments.Where(a => a.RiskLevel >= RiskLevel.High).Select(a => a.CustomerId))
            .ToList();
        if (customerIds.Count == 0)
        {
            return Array.Empty<HighRiskRiskCustomerDto>();
        }

        var customers = await dbContext.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.FullName, c.Mobile })
            .ToListAsync(ct);

        var assessmentByCustomer = latestAssessments.ToDictionary(a => a.CustomerId);
        return customers
            .Select(c =>
            {
                var od = operational.GetValueOrDefault(c.Id);
                var assessment = assessmentByCustomer.GetValueOrDefault(c.Id);
                return new HighRiskRiskCustomerDto(
                    c.Id, c.FullName, c.Mobile,
                    od.Count, od.MaxDays, bounced.GetValueOrDefault(c.Id),
                    od.Amount,
                    assessment?.Score, assessment?.RiskLevel, assessment?.Decision);
            })
            .OrderByDescending(r => r.RiskLevel ?? RiskLevel.VeryLow)
            .ThenBy(r => r.Score ?? int.MaxValue)
            .ThenByDescending(r => r.OverdueInstallmentCount + r.BouncedChequeCount)
            .ToList();
    }

    public async Task<IReadOnlyList<RiskWarningItemDto>> GetWarningsAsync(
        bool? unreadOnly, CancellationToken ct = default)
    {
        var query = dbContext.RiskWarnings.AsNoTracking();

        if (unreadOnly == true)
        {
            query = query.Where(w => !w.IsRead);
        }

        var warnings = await query
            .OrderByDescending(w => w.CreatedAt)
            .Take(200)
            .Select(w => new { w.Id, w.CustomerId, w.Type, w.Message, w.IsRead, w.CreatedAt })
            .ToListAsync(ct);

        var names = await CustomerNamesAsync(warnings.Select(w => w.CustomerId).Distinct().ToList(), ct);

        return warnings
            .Select(w => new RiskWarningItemDto(
                w.Id, w.CustomerId, names.GetValueOrDefault(w.CustomerId, "—"),
                w.Type, w.Message, w.IsRead, w.CreatedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<ManualReviewListItemDto>> GetManualReviewsAsync(
        ManualReviewStatus? status, CancellationToken ct = default)
    {
        var query = dbContext.ManualReviews.AsNoTracking();

        // No filter = the live queue (everything not yet decided).
        if (status is { } filter)
        {
            query = query.Where(r => r.Status == filter);
        }
        else
        {
            query = query.Where(r => r.Status != ManualReviewStatus.Approved && r.Status != ManualReviewStatus.Rejected);
        }

        var reviews = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(200)
            .Select(r => new
            {
                r.Id, r.CustomerId, r.AssessmentId, r.Status, r.RecommendedDecision, r.FinalDecision,
                r.AssignedToUserId, r.Note, r.CreatedAt, r.ResolvedAt,
                AssignedToName = r.AssignedToUser != null ? r.AssignedToUser.FullName : null,
                r.Assessment.Score, r.Assessment.RiskLevel,
                r.Assessment.CurrentDebtToman, r.Assessment.OverdueAmountToman,
                r.Assessment.OverdueCount, r.Assessment.ReturnedChequeCount,
                r.Assessment.TriggeredRulesJson,
            })
            .ToListAsync(ct);

        var names = await CustomerNamesAsync(reviews.Select(r => r.CustomerId).Distinct().ToList(), ct);

        return reviews
            .Select(r => new ManualReviewListItemDto(
                r.Id, r.CustomerId, names.GetValueOrDefault(r.CustomerId, "—"), r.AssessmentId,
                r.Score, r.RiskLevel, r.RecommendedDecision, r.FinalDecision, r.Status,
                r.AssignedToName,
                r.CurrentDebtToman, r.OverdueAmountToman, r.OverdueCount, r.ReturnedChequeCount,
                r.TriggeredRulesJson, r.Note, r.CreatedAt, r.ResolvedAt))
            .ToList();
    }

    private async Task<Dictionary<Guid, string>> CustomerNamesAsync(List<Guid> customerIds, CancellationToken ct) =>
        await dbContext.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.FullName, ct);

    public static string WarningTypeFa(RiskWarningType type) => type switch
    {
        RiskWarningType.LevelEscalation => "افزایش سطح ریسک",
        RiskWarningType.ScoreDrop => "افت امتیاز",
        RiskWarningType.NearCreditLimit => "نزدیک‌شدن به سقف اعتبار",
        RiskWarningType.RapidDebtGrowth => "رشد سریع بدهی",
        _ => "چک برگشتی جدید",
    };
}
