namespace Aqsat.Api.Contracts;

public sealed record AgencySettingsDto(
    string Code, string Name, string? City, string? InsurerName,
    int SettlementDeadlineDays, string LockScope, string DueDateRule, bool ShiftOnHoliday,
    int MaxInstallments, string ReminderDaysBefore, int MaxOpenTabs,
    decimal DefaultServiceFee, string ServiceFeeMode, int DefaultWriteOffDays, int RenewalAutoWatchLeadDays,
    /// <summary>docs/TASK-24-POLICY-NUMBER.md §4.3 — the code embedded in the official policy
    /// number. AgencyCodeLocked is true once at least one policy already exists.</summary>
    string? AgencyCode, bool AgencyCodeLocked,
    /// <summary>The installment-contract text the customer accepts on the portal — null means the
    /// system default is in effect.</summary>
    string? InstallmentContractText = null,
    /// <summary>The agency manager's mobile that danger-zone confirmation codes are SMS'd to.</summary>
    string? DangerZoneManagerMobile = null);

/// <summary>docs/TASK-24-POLICY-NUMBER.md §4.3 — a dedicated endpoint, not a field on the general
/// settings PUT, since it carries its own service-layer lock rule.</summary>
public sealed record UpdateAgencyCodeRequest(string AgencyCode);

/// <summary>ConfirmCode must equal the agency's own Code — a "type to confirm" guard against an
/// accidental click on an irreversible bulk action — and OtpCode must be the 6-digit code SMS'd
/// to the agency manager's mobile (owner decision 2026-09-02: destructive operations need a
/// second factor that is not the logged-in user's session).</summary>
public sealed record ClearAgencyDataRequest(string ConfirmCode, string? OtpCode = null);

public sealed record RequestDangerZoneOtpResponse(bool Sent);

/// <summary>docs/CUSTOMER-PORTAL-SPEC.md §3 — the agency's own customer-portal gateway (the
/// customer's down payment lands HERE, in the agency's own account; the inquiry fee's gateway is
/// the platform-level one). AgentMerchantId is only returned masked. A dedicated partial update,
/// not a field on the general PUT: the «تنظیمات درگاه پرداخت» sub-page saves only these fields,
/// so it can never clobber concurrent edits to the operational parameters.</summary>
public sealed record AgencyPaymentGatewayDto(
    string Name, string Code, string PaymentProvider, bool CustomerPortalEnabled,
    bool HasAgentMerchantId, string? AgentMerchantIdMasked, int PortalInvitationTtlHours);

public sealed record UpdateAgencyPaymentGatewayRequest(
    string PaymentProvider, bool CustomerPortalEnabled, string? AgentMerchantId, int PortalInvitationTtlHours);

/// <summary>«تنظیمات پنل پیامکی» — the agency's own api.ir key for sending SMS. SmsApiKey is only
/// returned masked, and null/whitespace on PUT keeps the stored one (the same write-only contract
/// as every other credential panel).</summary>
public sealed record AgencySmsPanelDto(
    string Name, string Code, bool HasSmsApiKey, string? SmsApiKeyMasked);

public sealed record UpdateAgencySmsPanelRequest(string? SmsApiKey);

public sealed record UpdateAgencySettingsRequest(
    string Name, string? City, string? InsurerName,
    int SettlementDeadlineDays, string LockScope, bool ShiftOnHoliday,
    int MaxInstallments, string ReminderDaysBefore, int MaxOpenTabs,
    decimal DefaultServiceFee, string ServiceFeeMode, int DefaultWriteOffDays, int RenewalAutoWatchLeadDays,
    /// <summary>Null keeps the stored text; empty string clears it back to the system default.</summary>
    string? InstallmentContractText = null,
    /// <summary>Null keeps the stored mobile; empty string clears it (danger-zone OTP then refuses
    /// to start until a new one is set).</summary>
    string? DangerZoneManagerMobile = null);
