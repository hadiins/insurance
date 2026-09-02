using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// Moving money between two of the agency's own cash boxes / bank accounts — exactly one "from"
/// side and one "to" side is set, never both of a side and never neither. Not tied to any Policy,
/// so IAuditableEntity.PolicyId uses the Guid.Empty sentinel (Expense precedent).
/// </summary>
public class FundTransfer : AgencyOwnedEntity, IAuditableEntity
{
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }

    public Guid? FromCashBoxId { get; set; }
    public CashBox? FromCashBox { get; set; }
    public Guid? FromBankAccountId { get; set; }
    public BankAccount? FromBankAccount { get; set; }
    public Guid? ToCashBoxId { get; set; }
    public CashBox? ToCashBox { get; set; }
    public Guid? ToBankAccountId { get; set; }
    public BankAccount? ToBankAccount { get; set; }

    Guid IAuditableEntity.PolicyId => Guid.Empty;

    string IAuditableEntity.DescribeChange(AuditAction action) => action switch
    {
        AuditAction.Created => "ثبت انتقال وجه",
        AuditAction.Updated => "ویرایش انتقال وجه",
        _ => "تغییر انتقال وجه",
    };
}

/// <summary>
/// One batch of marketer-commission payments (MarketersController.PayCommissions) recorded as an
/// actual money outflow — without it, paying a marketer only flipped CommissionEntry.Status and no
/// cash ever left a CashBox/BankAccount. PaymentBatchId matches the CommissionEntry.PaymentBatchId
/// values it paid. Not tied to any Policy, so IAuditableEntity.PolicyId uses the Guid.Empty
/// sentinel (Expense precedent).
/// </summary>
public class CommissionPayout : AgencyOwnedEntity, IAuditableEntity
{
    public Guid MarketerId { get; set; }
    public Marketer Marketer { get; set; } = default!;

    public Guid PaymentBatchId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaidOn { get; set; }

    public PaymentMethod MethodType { get; set; }
    public Guid? CashBoxId { get; set; }
    public CashBox? CashBox { get; set; }
    public Guid? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }
    public string? ReferenceNo { get; set; }

    Guid IAuditableEntity.PolicyId => Guid.Empty;

    string IAuditableEntity.DescribeChange(AuditAction action) => action switch
    {
        AuditAction.Created => "پرداخت پورسانت بازاریاب",
        AuditAction.Updated => "ویرایش پرداخت پورسانت بازاریاب",
        _ => "تغییر پرداخت پورسانت بازاریاب",
    };
}
