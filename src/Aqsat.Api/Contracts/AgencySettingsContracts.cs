namespace Aqsat.Api.Contracts;

public sealed record AgencySettingsDto(
    string Code, string Name, string? City, string? InsurerName,
    int SettlementDeadlineDays, string LockScope, string DueDateRule, bool ShiftOnHoliday,
    int MaxInstallments, string ReminderDaysBefore, int MaxOpenTabs,
    decimal DefaultServiceFee, string ServiceFeeMode, int DefaultWriteOffDays, int RenewalAutoWatchLeadDays,
    /// <summary>docs/TASK-24-POLICY-NUMBER.md §4.3 — the code embedded in the official policy
    /// number. AgencyCodeLocked is true once at least one policy already exists.</summary>
    string? AgencyCode, bool AgencyCodeLocked);

/// <summary>docs/TASK-24-POLICY-NUMBER.md §4.3 — a dedicated endpoint, not a field on the general
/// settings PUT, since it carries its own service-layer lock rule.</summary>
public sealed record UpdateAgencyCodeRequest(string AgencyCode);

/// <summary>ConfirmCode must equal the agency's own Code — a "type to confirm" guard against an
/// accidental click on an irreversible bulk action.</summary>
public sealed record ClearAgencyDataRequest(string ConfirmCode);

public sealed record UpdateAgencySettingsRequest(
    string Name, string? City, string? InsurerName,
    int SettlementDeadlineDays, string LockScope, bool ShiftOnHoliday,
    int MaxInstallments, string ReminderDaysBefore, int MaxOpenTabs,
    decimal DefaultServiceFee, string ServiceFeeMode, int DefaultWriteOffDays, int RenewalAutoWatchLeadDays);
