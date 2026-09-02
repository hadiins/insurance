using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>A bank account the agency receives transfers/cheques into — agency-defined, shown as a
/// dropdown on every non-cash receipt form. OpeningBalance is what the account held before the
/// system's first recorded flow — everything else is arithmetic over
/// Payments/Expenses/InsurerRemittances/FundTransfers/CommissionPayouts.</summary>
public class BankAccount : AgencyOwnedEntity
{
    public string BankName { get; set; } = default!;
    public string AccountNumber { get; set; } = default!;
    public string? AccountHolderName { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal OpeningBalance { get; set; }
}
