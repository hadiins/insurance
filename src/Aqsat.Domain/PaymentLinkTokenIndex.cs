using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// The installment-payment portal's only RLS-exempt row: maps a payment-link token to its agency
/// so an anonymous visitor can be scoped before any RLS read. Same design and same defence as
/// PortalInvitationTokenIndex (SQL Server applies security-policy predicates even inside scalar
/// function bodies, so this table is deliberately NOT covered by AgencyAccessPolicy). Carries
/// nothing but the token, the agency id and the link id: no customer data, no amounts. Written by
/// InstallmentPaymentLinkService in the same SaveChanges as the link itself; unique on Token, so a
/// soft-deleted link's token can never resolve a second time.
/// </summary>
public class PaymentLinkTokenIndex : Entity
{
    public string Token { get; set; } = default!;

    public Guid AgencyId { get; set; }

    /// <summary>Back-reference to the link the token belongs to — operator-side forensics (which
    /// link row a leaked token belonged to) without needing RLS-exempt access.</summary>
    public Guid LinkId { get; set; }
}
