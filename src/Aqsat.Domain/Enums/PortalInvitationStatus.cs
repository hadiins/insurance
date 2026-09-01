namespace Aqsat.Domain.Enums;

/// <summary>Lifecycle of a customer-portal invitation link (docs/CUSTOMER-PORTAL-SPEC §3 flow).
/// Expired is applied lazily on access, never by a background sweep — an Expired row is
/// immutable afterwards.</summary>
public enum PortalInvitationStatus : byte
{
    Pending = 1,
    Paid = 2,
    Expired = 3,
}
