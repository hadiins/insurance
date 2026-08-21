namespace Aqsat.Api.Contracts;

/// <summary>docs/TASKS.md Task 17 — rendered as "علی رضایی — ۱۹ مرداد ۱۰:۴۲ — ثبت پرداخت قسط ۲".
/// Description is composed at write time (AuditEntry.Description); this DTO only carries what the
/// UI needs to lay that line out.</summary>
public sealed record TimelineEntryDto(string ActorDisplayName, DateTimeOffset OccurredAt, string Description);
