using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>A physical/logical cash drawer the agency collects cash into — agency-defined, shown
/// as a dropdown on every cash receipt form (down payment, installment payment, full payment).</summary>
public class CashBox : AgencyOwnedEntity
{
    public string Name { get; set; } = default!;
    public bool IsActive { get; set; } = true;
}
