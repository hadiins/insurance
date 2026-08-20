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
