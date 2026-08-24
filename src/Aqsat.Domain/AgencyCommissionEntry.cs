using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// The agency's own commission from the insurer, tracked event-driven exactly like CommissionEntry
/// (marketer's own slice) but without a MarketerId — one slice per installment, plus one
/// down-payment slice, for installment policies; one single full-policy slice for non-installment
/// policies, since those have no per-installment settlement events to key off of.
/// </summary>
public class AgencyCommissionEntry : AgencyOwnedEntity, IAuditableEntity
{
    public Guid PolicyId { get; set; }
    public Policy Policy { get; set; } = default!;

    /// <summary>Null means this is the down-payment slice (installment policy) or the single
    /// full-policy slice (non-installment policy) — <see cref="IsFullPolicySlice"/> disambiguates.</summary>
    public Guid? InstallmentId { get; set; }
    public Installment? Installment { get; set; }

    /// <summary>True only for the single slice of a non-installment policy's full payment.</summary>
    public bool IsFullPolicySlice { get; set; }

    /// <summary>This slice's share of NetPremium.</summary>
    public decimal BasePortion { get; set; }

    /// <summary>Locked at generation time — a later AgencyCommissionRate change never affects this row.</summary>
    public decimal RatePercent { get; set; }
    public decimal Amount { get; set; }

    public CommissionStatus Status { get; set; } = CommissionStatus.Pending;

    public DateTimeOffset? EligibleAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    Guid IAuditableEntity.PolicyId => PolicyId;

    string IAuditableEntity.DescribeChange(AuditAction action) => action switch
    {
        AuditAction.Created => "ثبت سهم کارمزد نمایندگی",
        AuditAction.Updated => "ویرایش سهم کارمزد نمایندگی",
        _ => "تغییر سهم کارمزد نمایندگی",
    };
}
