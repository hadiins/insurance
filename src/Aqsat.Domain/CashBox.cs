using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>A physical/logical cash drawer the agency collects cash into — agency-defined, shown
/// as a dropdown on every cash receipt form (down payment, installment payment, full payment).
/// OpeningBalance is what the box held before the system's first recorded flow — everything else
/// is arithmetic over Payments/Expenses/InsurerRemittances/FundTransfers/CommissionPayouts.</summary>
public class CashBox : AgencyOwnedEntity
{
    public string Name { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public decimal OpeningBalance { get; set; }
}
