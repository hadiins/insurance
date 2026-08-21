namespace Aqsat.Domain.Enums;

public enum AuditAction : byte
{
    Created = 1,
    Updated = 2,
    Deleted = 3,
    PaymentRecorded = 4,
    LockForceReleased = 5,
    PaymentReversed = 6,

    /// <summary>A marketer viewed a customer/policy they introduced — docs/PHASE-1-SPEC.md §2.2:
    /// "every marketer view is audited, and the agency owner can see what their marketer looked at."</summary>
    Viewed = 7,
}
