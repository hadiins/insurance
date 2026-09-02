using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// Operational parameters per agency — never hard-coded constants (CLAUDE.md). 1:1 extension of
/// Organization, keyed directly on OrganizationId rather than the Entity Id/BizId pattern.
/// </summary>
public class OrgSettings
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;

    public int SettlementDeadlineDays { get; set; } = 3;
    public LockScope LockScope { get; set; } = LockScope.Full;
    public DueDateRule DueDateRule { get; set; } = DueDateRule.StartPlusNMonthsSameDay;
    public bool ShiftOnHoliday { get; set; } = true;
    public int MaxInstallments { get; set; } = 9;
    public string ReminderDaysBefore { get; set; } = "7,3,0";
    public int MaxOpenTabs { get; set; } = 12;

    public decimal DefaultServiceFee { get; set; }
    public ServiceFeeMode ServiceFeeMode { get; set; } = ServiceFeeMode.Fixed;

    /// <summary>docs/PHASE-1-SPEC.md §3.6 — after this many days overdue, an installment's unpaid
    /// balance counts as "سوخت نکول" (default write-off) in the P&amp;L. Configurable per agency, not
    /// a hard-coded constant.</summary>
    public int DefaultWriteOffDays { get; set; } = 30;

    /// <summary>docs/PHASE-1-SPEC.md §2.9 — how many days before a policy's own EndDate the daily
    /// job auto-creates a RenewalWatch for it. Configurable per agency, not a hard-coded
    /// constant.</summary>
    public int RenewalAutoWatchLeadDays { get; set; } = 60;

    // ---- Customer portal & payment gateway (docs/CUSTOMER-PORTAL-SPEC.md §3) ----

    /// <summary>The gateway this agency's CUSTOMERS pay the down payment through (goes to the
    /// agency's own account — the inquiry fee's owner-side gateway is PlatformPaymentSettings).
    /// Mock = simulated, no real money moves; the safe default before any credential exists.</summary>
    public PaymentProvider PaymentProvider { get; set; } = PaymentProvider.Mock;

    /// <summary>The agency's own merchant credential at the PSP. Null/empty = real-gateway calls
    /// fail closed. Never returned in clear by the panel — masked like ApiIrSettings.ApiKey.</summary>
    public string? AgentMerchantId { get; set; }

    /// <summary>This agency's own api.ir key (its SMS panel credential). Null/empty = fall back to
    /// the platform-level ApiIrSettings key, so a fresh agency sends exactly like before this
    /// existed. Never returned in clear by the panel — masked like ApiIrSettings.ApiKey.</summary>
    public string? SmsApiKey { get; set; }

    /// <summary>How long a portal invitation link stays valid after the agent creates it.</summary>
    public int PortalInvitationTtlHours { get; set; } = 72;

    /// <summary>Master switch for this agency's customer portal — the link-issuing endpoint
    /// refuses when false, regardless of anything else.</summary>
    public bool CustomerPortalEnabled { get; set; }

    /// <summary>The installment-contract text the customer accepts on the portal before paying
    /// the down payment (owner decision 2026-09-01). Null/empty = the default Persian text
    /// (InstallmentContractDefaults.DefaultText); each agency may edit its own.</summary>
    public string? InstallmentContractText { get; set; }

    /// <summary>The agency manager's mobile — destructive operations (danger-zone data wipe)
    /// SMS their confirmation code here, never to whoever happens to be logged in (owner decision
    /// 2026-09-02). Null/empty = the danger-zone OTP flow refuses to start.</summary>
    public string? DangerZoneManagerMobile { get; set; }

    public byte[] RowVersion { get; set; } = default!;
}

public static class InstallmentContractDefaults
{
    /// <summary>The fallback contract text shown on the portal when the agency has not written
    /// its own — describes rules, installment commitment and the 3-day settlement window.</summary>
    public const string DefaultText =
        """
        قرارداد فروش بیمه‌نامه به‌صورت اقساطی

        ۱. با پذیرش این قرارداد، بیمه‌گذار مبلغ کل بیمه‌نامه شامل حق بیمه و کارمزد خدمات را به‌صورت پیش‌پرداخت و اقساط ماهانه تأدیه می‌کند.
        ۲. تاریخ سررسید هر قسط، همان روز ماه صدور بیمه‌نامه است و پرداخت هر قسط در موعد آن الزامی است.
        ۳. در صورت تأخیر در پرداخت اقساط، نمایندگی مجاز است از ادامهٔ پوشش بیمه‌ای و صدور بیمه‌نامه‌های بعدی برای بیمه‌گذار خودداری کند.
        ۴. بیمه‌گذار تأیید می‌کند که اطلاعات ثبت‌شده در پرونده صحیح است و هرگونه تغییر را کتباً اطلاع خواهد داد.
        ۵. این قرارداد در چارچوب مقررات بیمه مرکزی جمهوری اسلامی ایران تنظیم شده است.
        """;
}
