using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// A long-lived per-customer link the SMS reminder carries so the customer can pay any open
/// installment online in the public portal ({Portal:PublicBaseUrl}/pay/{token}). Deliberately
/// separate from CustomerPortalInvitation, which is the TTL-limited verification-chain link —
/// this one lives as long as the reminder keeps refreshing it (rolling expiry from
/// OrgSettings.PaymentLinkTtlDays, applied lazily on access). Token is 43-char Base64Url from
/// 32 CSPRNG bytes, resolved through the RLS-exempt PaymentLinkTokenIndex before any RLS read.
/// One active link per customer — the unique filtered index below enforces it.
/// </summary>
public class CustomerPaymentLink : AgencyOwnedEntity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;

    public string Token { get; set; } = default!;

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Refreshed to now + PaymentLinkTtlDays every time the reminder job sends the link.
    /// Expiry is applied lazily on access (Expired rows stay immutable), like invitations.</summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? LastSentAtUtc { get; set; }

    /// <summary>Only Active links resolve. Revocation is an explicit agency action; re-sending the
    /// reminder creates a fresh link with a new token.</summary>
    public PaymentLinkStatus Status { get; set; } = PaymentLinkStatus.Active;
}
