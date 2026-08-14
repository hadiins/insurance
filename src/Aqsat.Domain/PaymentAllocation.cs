using Aqsat.Domain.Common;

namespace Aqsat.Domain;

public class PaymentAllocation : AgencyOwnedEntity
{
    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = default!;

    public Guid InstallmentId { get; set; }
    public Installment Installment { get; set; } = default!;

    public decimal Amount { get; set; }
}
