namespace Aqsat.Api.Contracts;

public sealed record CountdownRowDto(
    Guid InstallmentId,
    Guid PolicyId,
    string PolicyNumber,
    string CustomerFullName,
    int SeqNo,
    DateOnly DueDate,
    DateOnly SettlementDeadline,
    decimal Amount,
    decimal PaidAmount,
    decimal Balance,
    string Status,
    string Urgency);

/// <summary>docs/PHASE-1-SPEC.md §3.3: the shortfall is the dominant figure — what comes out of the
/// agent's pocket if collection doesn't catch up before the deadline.</summary>
public sealed record CountdownDashboardDto(
    decimal Owed,
    decimal Collected,
    decimal Shortfall,
    IReadOnlyList<CountdownRowDto> Rows);

/// <summary>The header notification bell's three counters in one call (owner decision
/// 2026-09-02): overdue installments needing action, renewal watches due for follow-up, and
/// incomplete customer profiles. A renewal watch counts as due once its expiry is inside its
/// own NotifyDaysBefore window (or already past).</summary>
public sealed record TodaySummaryDto(
    int OverdueInstallments,
    int DueRenewals,
    IncompleteProfileSummaryDto IncompleteProfiles);
