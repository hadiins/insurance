using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// الحاقیه — schema only in Phase 1 (docs/PHASE-1-SPEC.md §2.6); management UI is a later phase.
/// When AffectsInstallments is set, unsettled installments are recalculated and commission is
/// recalculated on the new base for future installments only — settled installments are never
/// touched. Description is about the endorsement, never about a person (CLAUDE.md rule 8).
/// </summary>
public class Endorsement : AgencyOwnedEntity, IAuditableEntity
{
    public Guid PolicyId { get; set; }
    public Policy Policy { get; set; } = default!;

    public string EndorsementNo { get; set; } = default!;
    public string Type { get; set; } = default!;
    public DateOnly IssueDate { get; set; }

    /// <summary>Can be negative.</summary>
    public decimal PremiumDelta { get; set; }
    public decimal ServiceFeeDelta { get; set; }
    public bool AffectsInstallments { get; set; }

    public string? Description { get; set; }

    Guid IAuditableEntity.PolicyId => PolicyId;

    string IAuditableEntity.DescribeChange(AuditAction action) => action switch
    {
        AuditAction.Created => $"ثبت الحاقیهٔ {EndorsementNo}",
        AuditAction.Updated => $"ویرایش الحاقیهٔ {EndorsementNo}",
        _ => $"تغییر الحاقیهٔ {EndorsementNo}",
    };
}
