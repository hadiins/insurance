namespace Aqsat.Api.Contracts;

/// <summary>Operator-facing view of one portal invitation (docs/CUSTOMER-PORTAL-SPEC.md §3). Stage
/// is the verification-chain stage — null/Empty for a plain inquiry-fee link that has not paid yet.</summary>
public sealed record PortalInvitationDto(
    Guid Id, Guid CustomerId, string Token, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc,
    string Status, decimal InquiryFeeToman, bool SmsSent, string? Stage = null);

/// <summary>The customer's latest credit report for the customer file's «گزارش اعتباری» card and
/// the issuance wizard's step-1 status strip. Report is null when no inquiry ever ran. A reusable
/// report (≤30 days, RawSuccess) is re-attached by the wizard without a new fee or inquiry.</summary>
public sealed record CustomerCreditReportDto(
    CreditReportDto? Report,
    bool IsReusableForIssuance,
    DateTimeOffset? ValidUntilUtc,
    Guid? FailedStandaloneInvitationId);

public sealed record CreatePortalInvitationRequest(Guid CustomerId);

/// <summary>The PUBLIC, unauthenticated view of an invitation — deliberately minimal: customer's
/// display name, the fee, status and expiry. No national ID, no agency internals, no token echo
/// beyond what the caller already holds. For a policy-verification link, Stage plus the
/// stage-gated extras (contract text, installment schedule, down-payment amount) are filled in —
/// the contract and installments only appear once the agency has approved, so the customer cannot
/// read terms of a policy that may still be cancelled.</summary>
public sealed record PublicPortalInfoDto(
    string CustomerDisplayName, decimal FeeToman, string Status, DateTimeOffset ExpiresAtUtc,
    string? Stage = null,
    string? PolicyNumber = null,
    decimal? DownPaymentAmountToman = null,
    string? ContractText = null,
    IReadOnlyList<PublicPortalInstallmentDto>? Installments = null);

public sealed record PublicPortalInstallmentDto(int SeqNo, DateOnly DueDate, decimal Amount);

/// <summary>POST /api/portal/{token}/pay result.</summary>
public sealed record PublicPortalPayResultDto(decimal PaidAmountToman, DateTimeOffset PaidAtUtc);

/// <summary>POST /api/portal/{token}/approve-contract result.</summary>
public sealed record PublicPortalStageResultDto(string Stage);

/// <summary>The operator-side view of one policy's verification chain (wizard step 3.5). Null
/// invitation fields mean no chain was ever started for the policy.</summary>
public sealed record PolicyVerificationDto(
    Guid? InvitationId, string? Token, string? Stage, string? Status, DateTimeOffset? ExpiresAtUtc,
    decimal? InquiryFeeToman, decimal? DownPaymentAmountToman,
    DateTimeOffset? AgencyDecisionAtUtc, DateTimeOffset? CustomerApprovedAtUtc,
    DateTimeOffset? DownPaymentPaidAtUtc, CreditReportDto? CreditReport, bool? SmsSent);

/// <summary>Structured api.ir credit data (UnpaidCheque + ActiveLoans). Every field nullable —
/// RawSuccess=false means sandboxed or failed and the UI must say so, not render zeros as
/// facts. Amounts are toman, Persian display handled client-side.</summary>
public sealed record CreditReportDto(
    int? ChequeCount, decimal? ChequeSumAmountToman, decimal? ChequeSumBouncedAmountToman,
    int? ActiveLoansCount, decimal? LoanTotalAmountToman, decimal? LoanDebtTotalAmountToman,
    decimal? LoanPastExpiredTotalAmountToman, decimal? LoanDeferredTotalAmountToman,
    decimal? LoanSuspiciousTotalAmountToman, decimal? LoanDishonoredAmountToman,
    bool RawSuccess, DateTimeOffset RetrievedAtUtc);

public sealed record AgencyVerificationDecisionRequest(bool Approve);

/// <summary>The PUBLIC, unauthenticated view of a customer's installment-payment link
/// (/pay/{token}) — display names and the open installment schedule only. No national ID, no
/// agency internals. The installment ids are payable through the matching POST endpoint.</summary>
public sealed record PublicPaymentLinkInfoDto(
    string CustomerDisplayName, string AgencyName, IReadOnlyList<PublicPaymentLinkInstallmentDto> Installments);

public sealed record PublicPaymentLinkInstallmentDto(
    Guid Id, int SeqNo, string PolicyNumber, DateOnly DueDate, decimal BalanceToman, bool IsOverdue);

/// <summary>POST /api/portal/pay/{token}/installments/{installmentId} result.</summary>
public sealed record PublicPaymentLinkPayResultDto(
    decimal PaidAmountToman, DateTimeOffset PaidAtUtc, string PolicyNumber, int SeqNo);

/// <summary>The operator-side status of a customer's installment-payment link — HasActiveLink
/// false when the customer has none (or it expired/was revoked).</summary>
public sealed record CustomerPaymentLinkDto(
    bool HasActiveLink, string? Token, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? LastSentAtUtc);
