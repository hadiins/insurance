using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// The public portal's only RLS-exempt row: maps a link token to its agency so an anonymous
/// visitor can be scoped before any RLS read (docs/CUSTOMER-PORTAL-SPEC.md §3). SQL Server
/// applies security-policy predicates even inside scalar function bodies, so
/// fn_PortalInvitationAgency-style lookups fail under a NULL session context — this table is
/// deliberately NOT covered by AgencyAccessPolicy. It carries nothing but the token, the agency
/// id and the invitation id: no customer data, no amounts, nothing worth scraping. Written by
/// PortalInvitationService in the same SaveChanges as the invitation itself; unique on Token,
/// so a soft-deleted invitation's token can never resolve a second time.
/// </summary>
public class PortalInvitationTokenIndex : Entity
{
    public string Token { get; set; } = default!;

    public Guid AgencyId { get; set; }

    /// <summary>Back-reference to the invitation the token belongs to — the scoped load reads
    /// CustomerPortalInvitations by Token anyway; this column is for operator-side forensics
    /// (which link row a leaked token belonged to) without needing RLS-exempt access.</summary>
    public Guid InvitationId { get; set; }
}
