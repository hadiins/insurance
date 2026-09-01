namespace Aqsat.Api.Contracts;

/// <summary>Operator-facing view of one portal invitation (docs/CUSTOMER-PORTAL-SPEC.md §3).</summary>
public sealed record PortalInvitationDto(
    Guid Id, Guid CustomerId, string Token, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc,
    string Status, decimal InquiryFeeToman, bool SmsSent);

public sealed record CreatePortalInvitationRequest(Guid CustomerId);

/// <summary>The PUBLIC, unauthenticated view of an invitation — deliberately minimal: customer's
/// display name, the fee, status and expiry. No national ID, no agency internals, no token echo
/// beyond what the caller already holds.</summary>
public sealed record PublicPortalInfoDto(
    string CustomerDisplayName, decimal FeeToman, string Status, DateTimeOffset ExpiresAtUtc);

/// <summary>POST /api/portal/{token}/pay result.</summary>
public sealed record PublicPortalPayResultDto(decimal PaidAmountToman, DateTimeOffset PaidAtUtc);
