using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// A payment is its own entity, never a field on an installment — one payment may span several
/// installments, one installment may take several payments (allocations). Idempotent: the unique
/// index on (InstallmentIdHint, PaidOn, Amount) treats a duplicate submission as success.
/// </summary>
public class Payment : AgencyOwnedEntity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;

    /// <summary>The installment the agent intended this payment for — used only for the dedupe
    /// index; actual distribution lives in PaymentAllocation.</summary>
    public Guid InstallmentIdHint { get; set; }

    public decimal Amount { get; set; }
    public DateOnly PaidOn { get; set; }
    public string Method { get; set; } = default!;
    public string? ReferenceNo { get; set; }

    public Guid RecordedByUserId { get; set; }

    public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
}
