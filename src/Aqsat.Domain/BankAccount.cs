using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>A bank account the agency receives transfers/cheques into — agency-defined, shown as a
/// dropdown on every non-cash receipt form.</summary>
public class BankAccount : AgencyOwnedEntity
{
    public string BankName { get; set; } = default!;
    public string AccountNumber { get; set; } = default!;
    public string? AccountHolderName { get; set; }
    public bool IsActive { get; set; } = true;
}
