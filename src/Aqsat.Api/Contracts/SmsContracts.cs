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
