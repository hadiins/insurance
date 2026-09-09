using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// The agency's manual credit limit for one customer — overrides the computed recommendation
/// (docs Phase 2A §14). One row per customer per agency. The reason describes the decision, not
/// the person (rule 8).
/// </summary>
public class CustomerCreditLimit : AgencyOwnedEntity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;

    public decimal LimitToman { get; set; }

    public string? Reason { get; set; }

    public Guid SetByUserId { get; set; }

    public DateTimeOffset SetAt { get; set; }
}
