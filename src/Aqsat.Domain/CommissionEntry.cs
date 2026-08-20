using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// One slice per installment, plus one down-payment slice (InstallmentId null) — docs/PHASE-1-SPEC.md
/// §2.8/§3.5. Base is NetPremium only, never TotalReceivable: the service fee belongs to the agency,
/// not the marketer. RatePercent is copied at issuance/generation time and never changed afterward,
/// so a later rate change never alters an existing entry. A slice becomes Payable only when its
/// installment fully settles; partial default is proportional with no clawback.
/// </summary>
public class CommissionEntry : AgencyOwnedEntity, IAuditableEntity
{
    public Guid MarketerId { get; set; }
    public Marketer Marketer { get; set; } = default!;

    public Guid PolicyId { get; set; }
    public Policy Policy { get; set; } = default!;

    /// <summary>Null means this is the down-payment slice.</summary>
    public Guid? InstallmentId { get; set; }
    public Installment? Installment { get; set; }

    /// <summary>This slice's share of NetPremium.</summary>
    public decimal BasePortion { get; set; }

    /// <summary>Locked at generation time — later MarketerRate changes never affect this row.</summary>
    public decimal RatePercent { get; set; }
    public decimal Amount { get; set; }

    public CommissionStatus Status { get; set; } = CommissionStatus.Pending;

    /// <summary>The moment the linked installment fully settled (or immediately, for the
    /// down-payment slice).</summary>
    public DateTimeOffset? EligibleAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public Guid? PaymentBatchId { get; set; }

    Guid IAuditableEntity.PolicyId => PolicyId;

    string IAuditableEntity.DescribeChange(AuditAction action) => action switch
    {
        AuditAction.Created => "ثبت سهم پورسانت بازاریاب",
        AuditAction.Updated => "ویرایش سهم پورسانت بازاریاب",
        _ => "تغییر سهم پورسانت بازاریاب",
    };
}
