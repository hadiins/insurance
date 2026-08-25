using System.Text.Json.Serialization;

namespace Aqsat.Domain.Enums;

/// <summary>The frontend sends this as a JSON string ("Cash"/"BankTransfer"/"Cheque"), never a raw
/// number — [ApiController]'s default System.Text.Json config only accepts enums by their numeric
/// value unless told otherwise, so this converter is required, not cosmetic.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentMethod : byte
{
    Cash = 1,
    BankTransfer = 2,
    Cheque = 3,

    /// <summary>The agency's own card terminal, wired directly to the insurer's account — the
    /// money never touches the agency's CashBox or BankAccount, so Payment.CashBoxId/BankAccountId
    /// stay null for this method. Which insurer it was is already known from the Policy.</summary>
    PosDirect = 4,
}
