namespace Aqsat.Api.Contracts;

/// <summary>The four headline figures on the Today dashboard. Every optional figure is optional
/// because it is genuinely unknown (no baseline to compare against, no goal configured) — never
/// because a failure was swallowed into a zero.</summary>
public sealed record TodayFiguresDto(
    decimal CollectedToday,
    decimal? CollectedTodayDeltaPct,
    IReadOnlyList<decimal> CollectedLast7Days,
    int DueTodayCount,
    int DueTodayUnpaidCount,
    int DueTodaySettledCount,
    decimal Overdue30Amount,
    int Overdue30Count,
    int ChequesInFlightCount,
    int ChequesBouncedCount);

/// <summary>One column of the «روند وصول» chart: what was actually collected that day and how much
/// of that day's goal was still outstanding. GoalRemaining is zero when no goal is configured, so
/// the chart draws the actual series alone.</summary>
public sealed record TodayTrendPointDto(
    DateOnly Date,
    decimal Collected,
    decimal GoalRemaining);

public sealed record TodayGoalDto(
    decimal? MonthlyGoal,
    decimal? DailyTarget,
    decimal MonthCollected,
    decimal? MonthProgressPct);

/// <summary>A row of the «یادآوری‌های پیامکی» feed. Tone is one of ok/warn/bad — the same three the
/// design's dot classes use. Every row is derived from a real record (ReminderLog, PaymentCheque,
/// Payment); none is a placeholder. Rows carry no timestamp on purpose: several aggregate several
/// records, and the honest qualifier ("امروز") belongs in Detail rather than a fabricated instant.</summary>
public sealed record TodayActivityDto(
    string Kind,
    string Tone,
    string Title,
    string Detail);

/// <summary>«آخرین همگام‌سازی با فناوران». LastSyncAt is null when no batch carries a timestamp
/// (batches committed before that column existed) — the UI says «نامشخص» instead of inventing a
/// time.</summary>
public sealed record TodaySyncDto(
    DateTimeOffset? LastSyncAt,
    int? LastSyncAgoMinutes,
    int OpenMismatchCount);

public sealed record TodayDashboardDto(
    TodayFiguresDto Figures,
    IReadOnlyList<TodayTrendPointDto> Trend,
    TodayGoalDto Goal,
    IReadOnlyList<TodayActivityDto> Activity,
    TodaySyncDto Sync,
    int ActiveUsers);

/// <summary>Sidebar badge counts. Each is a call to action, not a total — see NavBadgesController.</summary>
public sealed record NavBadgesDto(
    int InstallmentWorklist,
    int Cheques,
    int RiskReviews,
    int RenewalWatches);
