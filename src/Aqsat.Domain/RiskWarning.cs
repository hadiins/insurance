using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// Early warning (docs Phase 2A §19) raised while assessing, by comparing the new assessment with
/// the customer's previous one: risk-level escalation, notable score drop, approaching the credit
/// limit, rapid debt growth, or a new bounced cheque. The Persian message is composed at write
/// time (rule 31) and carries the reasons inline — no free text about the person (rule 8).
/// </summary>
public class RiskWarning : AgencyOwnedEntity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;

    /// <summary>The assessment whose comparison raised this warning.</summary>
    public Guid AssessmentId { get; set; }
    public RiskAssessment Assessment { get; set; } = default!;

    public RiskWarningType Type { get; set; }

    public string Message { get; set; } = default!;

    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
