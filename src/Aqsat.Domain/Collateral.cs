using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

public class Collateral : AgencyOwnedEntity
{
    public Guid PolicyId { get; set; }
    public Policy Policy { get; set; } = default!;

    public CollateralType Type { get; set; }
    public string? SayadId { get; set; }
    public string? BankName { get; set; }
    public decimal Amount { get; set; }
    public DateOnly? DueDate { get; set; }
    public CollateralStatus Status { get; set; } = CollateralStatus.Held;

    /// <summary>ChequeColor lookup result — later phase.</summary>
    public string? ColorCode { get; set; }
    public DateTimeOffset? CheckedAt { get; set; }
}
