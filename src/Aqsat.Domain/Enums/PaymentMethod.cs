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

    /// <summary>The customer's portal down-payment payment through the agency's PSP — recorded
    /// automatically by PolicyVerificationService, never chosen by an operator in a receipt form.
    /// Like PosDirect, no CashBox or BankAccount is touched.</summary>
    Online = 5,
}

    /// <summary>Well-known values of Payment.Method (the free-text display column) that code keys
    /// on — defined once in Domain so controllers and services share the exact same string.</summary>
    public static class WellKnownPaymentMethods
    {
        /// <summary>The marker ReportsController's cash-basis P&amp;L filters on to recognize a
        /// down-payment Payment (which carries no PaymentAllocation rows).</summary>
        public const string DownPayment = "پیش‌پرداخت";

        /// <summary>An installment paid by the customer through the public /pay/{token} portal —
        /// recorded automatically by InstallmentPaymentLinkService, never chosen by an operator.</summary>
        public const string OnlineInstallment = "قسط (پورتال)";
    }
