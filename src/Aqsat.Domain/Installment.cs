using System.ComponentModel.DataAnnotations.Schema;
using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

public class Installment : AgencyOwnedEntity, IAuditableEntity
{
    public Guid PolicyId { get; set; }
    public Policy Policy { get; set; } = default!;

    public int SeqNo { get; set; }

    public DateOnly DueDate { get; set; }

    /// <summary>DueDate + OrgSettings.SettlementDeadlineDays, shifted off holidays. The deadline
    /// shifts, never the due date.</summary>
    public DateOnly SettlementDeadline { get; set; }

    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public InstallmentStatus Status { get; set; } = InstallmentStatus.Unpaid;

    /// <summary>When remitted to the insurer.</summary>
    public DateTimeOffset? RemittedAt { get; set; }

    /// <summary>Set the first time an agent overrides Amount or DueDate after generation
    /// (docs/PHASE-1-SPEC.md §2.7/niaz #3).</summary>
    public bool IsManuallyEdited { get; set; }

    [NotMapped]
    public decimal Balance => Amount - PaidAmount;

    Guid IAuditableEntity.PolicyId => PolicyId;

    string IAuditableEntity.DescribeChange(AuditAction action) => action switch
    {
        AuditAction.Created => $"ثبت قسط شمارهٔ {SeqNo}",
        AuditAction.Updated => $"ویرایش قسط شمارهٔ {SeqNo}",
        _ => $"تغییر قسط شمارهٔ {SeqNo}",
    };
}
