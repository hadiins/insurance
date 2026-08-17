using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

public class Policy : AgencyOwnedEntity, IAuditableEntity
{
    public string PolicyNumber { get; set; } = default!;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;

    public Guid VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = default!;

    /// <summary>Raw, as imported — resolved against ContractTemplate to determine IsInstallment.</summary>
    public string ContractName { get; set; } = default!;

    public bool IsInstallment { get; set; }

    public DateOnly IssueDate { get; set; }

    /// <summary>Drives every due date: DueDate(n) = StartDate + n months, same day-of-month.</summary>
    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    /// <summary>Toman (imports arrive in rials — divide by 10 at the import boundary).</summary>
    public decimal TotalPremium { get; set; }

    /// <summary>A free amount the agent picks, not a percentage — never enforce a ratio.</summary>
    public decimal DownPayment { get; set; }

    public int InstallmentCount { get; set; }

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
