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

    /// <summary>One hop of the installment-issuance verification chain (fee paid, credit report
    /// retrieved, agency approved/rejected, customer approved the contract, down payment paid
    /// online) — the description carries which hop and the policy number.</summary>
    PolicyVerification = 8,

    /// <summary>Credit-risk events (docs Phase 2A §27): assessment created, credit limit changed,
    /// risk settings changed — the description distinguishes which and carries the figures.</summary>
    RiskAssessed = 9,

    /// <summary>A manual-review case moved: assigned, more-info requested, or decided (docs Phase
    /// 2A §15/§27) — the description carries the case's final decision and note.</summary>
    ManualReviewDecided = 10,

    /// <summary>A cross-agency risk lookup ran (Phase 2B-1, owner decision: internal log only — the
    /// source agency gets no report of who queried). The description carries the queried key and
    /// the result count, never any shared figure.</summary>
    NetworkRiskLookup = 11,

    /// <summary>An agency revoked a customer's installment-payment link (the SMS link to the public
    /// pay page) — the description carries the customer's name and the revoked token prefix.</summary>
    PaymentLinkRevoked = 12,
}
