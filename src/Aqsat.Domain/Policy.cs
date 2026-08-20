using System.ComponentModel.DataAnnotations.Schema;
using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

public class Policy : AgencyOwnedEntity, IAuditableEntity
{
    public string PolicyNumber { get; set; } = default!;

    public Guid InsuranceLineId { get; set; }
    public InsuranceLine InsuranceLine { get; set; } = default!;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;

    /// <summary>Only for lines where InsuranceLine.RequiresVehicle — never force this onto every
    /// policy (CLAUDE.md "Multi-line from day one").</summary>
    public Guid? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    /// <summary>Only for lines where InsuranceLine.RequiresProperty (آتش‌سوزی and similar).</summary>
    public Guid? PropertySubjectId { get; set; }
    public PropertySubject? PropertySubject { get; set; }

    /// <summary>Raw, as imported — resolved against ContractTemplate to determine IsInstallment.</summary>
    public string ContractName { get; set; } = default!;

    public bool IsInstallment { get; set; }

    public DateOnly IssueDate { get; set; }

    /// <summary>Drives every due date: DueDate(n) = StartDate + n months, same day-of-month.</summary>
    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    // Money, in TOMAN, in this exact order — the order itself is a business rule
    // (docs/PHASE-1-SPEC.md §2.5/§5): premium -> service fee -> down payment -> installments.

    /// <summary>حق بیمه — the commission base. Never TotalReceivable.</summary>
    public decimal NetPremium { get; set; }

    /// <summary>کارمزد خدمات — the agency's own fee, added after the premium, never part of the
    /// marketer commission base.</summary>
    public decimal ServiceFee { get; set; }

    /// <summary>= NetPremium + ServiceFee. What the customer owes; installments divide this.</summary>
    [NotMapped]
    public decimal TotalReceivable => NetPremium + ServiceFee;

    /// <summary>A free amount the agent picks, not a percentage — never enforce a ratio.</summary>
    public decimal DownPayment { get; set; }

    /// <summary>= TotalReceivable - DownPayment.</summary>
    [NotMapped]
    public decimal FinancedAmount => TotalReceivable - DownPayment;

    public int InstallmentCount { get; set; }

    /// <summary>What the insurer pays the agency — for the P&L report, not the customer's balance.</summary>
    public decimal? AgencyCommissionPercent { get; set; }
    public decimal? AgencyCommissionAmount { get; set; }

    /// <summary>Locked at issuance — a later MarketerRate change never alters this policy's
    /// CommissionEntry rows.</summary>
    public Guid? MarketerId { get; set; }
    public Marketer? Marketer { get; set; }
    public decimal? MarketerRatePercent { get; set; }

    public string? PreviousInsurer { get; set; }
    public bool IsRenewal { get; set; }

    public PolicyStatus Status { get; set; } = PolicyStatus.Active;

    public Guid? ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }

    Guid IAuditableEntity.PolicyId => Id;

    string IAuditableEntity.DescribeChange(AuditAction action) => action switch
    {
        AuditAction.Created => $"ثبت بیمه‌نامهٔ {PolicyNumber}",
        AuditAction.Updated => $"ویرایش بیمه‌نامهٔ {PolicyNumber}",
        _ => $"تغییر بیمه‌نامهٔ {PolicyNumber}",
    };
}
