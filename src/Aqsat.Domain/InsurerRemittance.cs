using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// The agency's own outgoing payment to the insurer — remitting installment/premium money already
/// collected from customers, within the settlement deadline (CLAUDE.md's "3-day" rule). Distinct
/// from Expense (the agency's own operating costs): this is money that was never the agency's to
/// keep. Which insurer/line it was for is already known from each Line's Policy — no separate
/// insurer field on the batch itself. Not tied to one Policy, so IAuditableEntity.PolicyId uses the
/// Guid.Empty sentinel (AgencySettingsController.ClearData precedent).
/// </summary>
public class InsurerRemittance : AgencyOwnedEntity, IAuditableEntity
{
    public DateOnly Date { get; set; }

    /// <summary>Denormalized sum of Lines' Amount — kept in sync at write time, never recomputed
    /// live, same pattern PaymentCheque/Payment.Amount already establishes for this codebase.</summary>
    public decimal Amount { get; set; }

    public PaymentMethod MethodType { get; set; }
    public Guid? CashBoxId { get; set; }
    public CashBox? CashBox { get; set; }
    public Guid? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }
    public string? ReferenceNo { get; set; }

    public ICollection<InsurerRemittanceLine> Lines { get; set; } = new List<InsurerRemittanceLine>();

    Guid IAuditableEntity.PolicyId => Guid.Empty;

    string IAuditableEntity.DescribeChange(AuditAction action) => action switch
    {
        AuditAction.Created => "ثبت پرداخت به بیمه‌گر",
        AuditAction.Updated => "ویرایش پرداخت به بیمه‌گر",
        _ => "تغییر پرداخت به بیمه‌گر",
    };
}

/// <summary>One installment (or a down-payment/full-payment slice, InstallmentId null) folded into
/// a remittance batch — a batch commonly spans several policies at once.</summary>
public class InsurerRemittanceLine : AgencyOwnedEntity
{
    public Guid InsurerRemittanceId { get; set; }
    public InsurerRemittance InsurerRemittance { get; set; } = default!;

    public Guid PolicyId { get; set; }
    public Policy Policy { get; set; } = default!;

    public Guid? InstallmentId { get; set; }
    public Installment? Installment { get; set; }

    public decimal Amount { get; set; }
}
