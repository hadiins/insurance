namespace Aqsat.Domain.Enums;

/// <summary>Lifecycle of a customer installment-payment link. Expired is applied lazily on
/// access, never by a background sweep — an Expired row is immutable afterwards.</summary>
public enum PaymentLinkStatus : byte
{
    Active = 1,
    Revoked = 2,
    Expired = 3,
}
