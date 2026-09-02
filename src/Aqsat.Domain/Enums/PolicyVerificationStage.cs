namespace Aqsat.Domain.Enums;

/// <summary>
/// The staged verification chain an installment policy's portal invitation walks between wizard
/// steps 3 and 4 (owner decision 2026-09-01): fee payment → credit inquiries → agency decision →
/// customer contract approval → down payment. Rejected is terminal — the policy is cancelled with
/// it. Values are stored on CustomerPortalInvitation.Stage; PortalInvitationStatus keeps tracking
/// only the fee-payment lifecycle it always tracked.
/// </summary>
public enum PolicyVerificationStage : byte
{
    /// <summary>The link exists but the inquiry fee is not paid yet — identical meaning to
    /// PortalInvitationStatus.Pending, kept as the chain's explicit zero state.</summary>
    FeePending = 0,

    FeePaid = 1,
    ReportReady = 2,
    AgencyApproved = 3,
    CustomerApproved = 4,
    Completed = 5,
    Rejected = 6,
}
