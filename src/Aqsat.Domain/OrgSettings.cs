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

    public byte[] RowVersion { get; set; } = default!;
}
