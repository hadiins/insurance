using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// One collection contact attempt against a policy (or one of its installments). This is the record
/// behind the «آخرین تماس» / «بدون تماس» signals and, when <see cref="Outcome"/> is
/// <see cref="ContactOutcome.Promised"/>, behind «قول پرداخت» — a promise is a contact outcome, not
/// a separate entity, so the two can never disagree about what the agent was told.
///
/// No free-text field exists here on purpose (CLAUDE.md rule 8): every column is an enum, a date or
/// an amount. Whether a promise is broken is derived (PromisedOn &lt; today and a balance still
/// outstanding), never stored — the same way the settlement countdown derives urgency.
/// </summary>
public class CollectionContact : AgencyOwnedEntity, IAuditableEntity
{
    public Guid PolicyId { get; set; }
    public Policy Policy { get; set; } = default!;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;

    /// <summary>Set when the contact was about one specific installment; null for a policy-level
    /// call.</summary>
    public Guid? InstallmentId { get; set; }
    public Installment? Installment { get; set; }

    public ContactChannel Channel { get; set; }
    public ContactOutcome Outcome { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Both null or both set (enforced by a check constraint); the write service also
    /// requires them exactly when <see cref="Outcome"/> is Promised.</summary>
    public DateOnly? PromisedOn { get; set; }
    public decimal? PromisedAmount { get; set; }

    public Guid RecordedByUserId { get; set; }

    Guid IAuditableEntity.PolicyId => PolicyId;

    /// <summary>The audit row is written by AppDbContext's SaveChangesAsync override, so it lands in
    /// the same transaction as the contact itself and cannot be forgotten (rule 29). Its action is
    /// Created — the entity IS the logged event — and EntityType "CollectionContact" is what
    /// separates a logged call from any other creation in the audit trail.</summary>
    string IAuditableEntity.DescribeChange(AuditAction action) => action switch
    {
        AuditAction.Created => "ثبت تماس وصول",
        _ => "تغییر تماس وصول",
    };
}
