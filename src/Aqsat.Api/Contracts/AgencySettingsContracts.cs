namespace Aqsat.Api.Contracts;

public sealed record AgencySettingsDto(
    string Code, string Name, string? City, string? InsurerName,
    int SettlementDeadlineDays, string LockScope, string DueDateRule, bool ShiftOnHoliday,
    int MaxInstallments, string ReminderDaysBefore, int MaxOpenTabs,
    decimal DefaultServiceFee, string ServiceFeeMode, int DefaultWriteOffDays, int RenewalAutoWatchLeadDays);

/// <summary>ConfirmCode must equal the agency's own Code — a "type to confirm" guard against an
/// accidental click on an irreversible bulk action.</summary>
public sealed record ClearAgencyDataRequest(string ConfirmCode);

public sealed record UpdateAgencySettingsRequest(
    string Name, string? City, string? InsurerName,
    int SettlementDeadlineDays, string LockScope, bool ShiftOnHoliday,
    int MaxInstallments, string ReminderDaysBefore, int MaxOpenTabs,
    decimal DefaultServiceFee, string ServiceFeeMode, int DefaultWriteOffDays, int RenewalAutoWatchLeadDays);
