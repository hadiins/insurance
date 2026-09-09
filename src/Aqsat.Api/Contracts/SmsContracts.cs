namespace Aqsat.Api.Contracts;

/// <summary>niaz #13 — manual filtered send: "the operator filters by date range, status, line,
/// marketer, amount → preview count and cost → confirm → send."</summary>
public sealed record SmsFilterRequest(
    DateOnly? DueFrom, DateOnly? DueTo, string? Status, Guid? InsuranceLineId, Guid? MarketerId,
    decimal? MinAmount, decimal? MaxAmount);

public sealed record SmsPreviewResultDto(int Count, decimal EstimatedCostToman);

public sealed record SmsSendResultDto(int SentCount, int SkippedCount, int AlreadySentTodayCount);

public sealed record ReminderLogDto(
    Guid Id, Guid? InstallmentId, string? PolicyNumber, int? SeqNo, string RecipientType, string Mobile,
    int OffsetDays, string Status, DateTimeOffset SentAt);

/// <summary>گزارش تحویل پیامک — aggregated over every ReminderLog row, not just the last page of
/// the outbox, so the delivery rate is accurate even once the log is thousands of rows long.</summary>
public sealed record SmsDeliveryReportDto(
    int TotalSent, int TotalFailed, int CustomerRecipientCount, int MarketerRecipientCount, int InstallmentReminderCount, int RenewalReminderCount);

/// <summary>گزارش اثربخشی پیامک — whether reminders actually led to payment. A reminder is
/// "effective" when an allocation for its installment landed within attributionDays of the send;
/// every channel counts (an agent-recorded cash receipt after the SMS is still the SMS working).
/// AttributedCollected sums only the in-window allocation amounts, and EstimatedCost is the
/// campaign's cost at the standard per-message rate — an estimate, the panel's real price may
/// differ.</summary>
public sealed record SmsEffectivenessSummaryDto(
    int RemindersSent, int RemindersFailed, int DistinctInstallments,
    int PaidWithinWindow, decimal EffectivenessRate,
    decimal AttributedCollectedToman, decimal EstimatedCostToman);

/// <summary>Per-offset rows are intentionally independent: a payment that lands inside the
/// 7-day-offset reminder's window usually also lands inside the same installment's 3-day-offset
/// window. Each row answers its own question — "of reminders sent at this offset, how many were
/// followed by a payment within the window" — so overlaps across rows are expected, not a bug.</summary>
public sealed record SmsEffectivenessOffsetRow(int OffsetDays, int Sent, int Paid, decimal Rate);

public sealed record SmsEffectivenessMonthRow(string JalaliMonthKey, string MonthLabel, int Sent, int Paid, decimal Rate);

public sealed record SmsEffectivenessReportDto(
    SmsEffectivenessSummaryDto Summary,
    IReadOnlyList<SmsEffectivenessOffsetRow> OffsetRows,
    IReadOnlyList<SmsEffectivenessMonthRow> MonthRows);

/// <summary>One reminded installment: what was sent, and what (if anything) it collected.</summary>
public sealed record SmsEffectivenessDetailRow(
    string PolicyNumber, string CustomerFullName, int SeqNo, DateOnly DueDate,
    int ReminderCount, string Offsets,
    DateTimeOffset FirstReminderAt, DateOnly? FirstPaidOnAfterReminder,
    int? DaysToPay, decimal CollectedInWindowToman, string InstallmentStatus);

public sealed record SmsEffectivenessDetailPageDto(int TotalCount, IReadOnlyList<SmsEffectivenessDetailRow> Rows);
