using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Risk;

namespace Aqsat.Api.Contracts;

/// <summary>
/// GET /api/risk/network — one agency's status row for the queried person (Phase 2B-1, owner
/// decision: status only). IsEnabled=false is the switch-off answer, explicitly distinct from an
/// empty Results list so the UI never mistakes "disabled" for "no history" (rule 17).
/// </summary>
public sealed record NetworkRiskLookupApiDto(
    bool IsEnabled,
    string QueryKind,
    IReadOnlyList<NetworkRiskResultApiDto> Results);

/// <summary>
/// Deliberately carries NO amounts and NO identity fields (no name, mobile, policy number, not
/// even the customer/agency GUIDs) — the queried key is the only thing representing the person.
/// </summary>
public sealed record NetworkRiskResultApiDto(
    string AgencyName,
    string? InsurerName,
    bool IsOwnAgency,
    int Score,
    string RiskLevel,
    string RiskLevelKey,
    string Decision,
    string DecisionKey,
    int OverdueCount,
    int MaxDaysOverdue,
    int ReturnedChequeCount,
    decimal OnTimeRatePercent,
    int TenureMonths,
    int SettledInstallmentCount,
    DateTimeOffset CalculatedAtUtc);

public sealed record RiskNetworkSettingsDto(bool IsEnabled, DateTimeOffset? UpdatedAtUtc);

public sealed record UpdateRiskNetworkSettingsRequest(bool IsEnabled);
