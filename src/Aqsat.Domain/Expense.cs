using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// The agency's own operating costs (rent, salaries, utilities, …) — never a discount or refund to
/// a customer, and Title describes the expense itself, never a person (CLAUDE.md rule 8). Not tied
/// to any Policy, so IAuditableEntity.PolicyId uses the Guid.Empty sentinel already established in
/// AgencySettingsController.ClearData for agency-wide, non-policy actions.
/// </summary>
public class Expense : AgencyOwnedEntity, IAuditableEntity
{
    public string Title { get; set; } = default!;
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }

    public Guid CategoryId { get; set; }
    public ExpenseCategory Category { get; set; } = default!;

    public PaymentMethod MethodType { get; set; }
    public Guid? CashBoxId { get; set; }
    public CashBox? CashBox { get; set; }
    public Guid? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    Guid IAuditableEntity.PolicyId => Guid.Empty;

    string IAuditableEntity.DescribeChange(AuditAction action) => action switch
    {
        AuditAction.Created => "ثبت هزینه",
        AuditAction.Updated => "ویرایش هزینه",
        _ => "تغییر هزینه",
    };
}
