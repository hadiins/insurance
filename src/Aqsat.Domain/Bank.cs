using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// فهرست اسامی بانک‌های عامل چک — the drawee-bank names offered as a dropdown on cheque
/// registration forms. Distinct from BankAccount (the agency's own accounts): this is just the
/// agency-editable list of bank names, seeded with the standard Iranian banks on first read.
/// </summary>
public class Bank : AgencyOwnedEntity
{
    public string Name { get; set; } = default!;
    public bool IsActive { get; set; } = true;
}
