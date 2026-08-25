using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// A cheque received from a customer toward a payment (down payment, installment, or full
/// payment) — distinct from Collateral, which covers only guarantee/collateral cheques never meant
/// to be cashed. One cheque per Payment. Held physically in a CashBox until deposited; reuses
/// CollateralStatus (Held/AtBank/Cleared/Bounced) since the lifecycle is identical. A Bounced
/// transition drives the same reversal PaymentsController.Reverse already performs
/// (PaymentReversalService).
/// </summary>
public class PaymentCheque : AgencyOwnedEntity, IAuditableEntity
{
    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = default!;

    /// <summary>Denormalized from the underlying Payment at creation time — CLAUDE.md rule 28
    /// ("every audit row carries PolicyId") and avoids re-deriving it through
    /// InstallmentIdHint/Allocations on every read.</summary>
    public Guid PolicyId { get; set; }
    public Policy Policy { get; set; } = default!;

    public string ChequeNumber { get; set; } = default!;

    /// <summary>بانک عامل — the drawee bank the cheque is drawn on, never the agency's own
    /// BankAccount (that stays on Payment.BankAccountId for once it's actually deposited).</summary>
    public string BankName { get; set; } = default!;

    public DateOnly DueDate { get; set; }

    /// <summary>تحویل‌دهنده — who physically handed the cheque over, may differ from the customer.</summary>
    public string PresenterName { get; set; } = default!;

    /// <summary>Physical storage location while Held — CLAUDE.md-style per-agency CashBox, same
    /// entity Stage 1/7 already introduced for cash receipts.</summary>
    public Guid CashBoxId { get; set; }
    public CashBox CashBox { get; set; } = default!;

    public CollateralStatus Status { get; set; } = CollateralStatus.Held;

    Guid IAuditableEntity.PolicyId => PolicyId;

    string IAuditableEntity.DescribeChange(AuditAction action) => action switch
    {
        AuditAction.Created => "ثبت چک دریافتی",
        AuditAction.Updated => "تغییر وضعیت چک دریافتی",
        _ => "تغییر چک دریافتی",
    };
}
