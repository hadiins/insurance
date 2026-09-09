using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// A case the rule engine could not decide on its own (docs Phase 2A §15) — created when the
/// decision is MANUAL_REVIEW. Notes describe the decision and the evidence, never the person
/// (rule 8).
/// </summary>
public class ManualReview : AgencyOwnedEntity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;

    public Guid AssessmentId { get; set; }
    public RiskAssessment Assessment { get; set; } = default!;

    public ManualReviewStatus Status { get; set; } = ManualReviewStatus.Pending;

    /// <summary>The rule engine's recommendation, kept as the snapshot — the reviewer may overrule it.</summary>
    public RiskDecision RecommendedDecision { get; set; }

    /// <summary>The reviewer's final call; null while the case is open.</summary>
    public RiskDecision? FinalDecision { get; set; }

    public Guid? AssignedToUserId { get; set; }
    public AppUser? AssignedToUser { get; set; }

    /// <summary>Describes the evidence/decision — never free text about the person (rule 8).</summary>
    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
