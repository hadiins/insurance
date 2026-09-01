using Aqsat.Domain.Enums;

namespace Aqsat.Application.Payments;

/// <summary>Result of charging an amount — vendor details stay behind IPaymentGateway.</summary>
public readonly record struct GatewayPaymentResult(bool Succeeded, decimal? PaidAmountToman, string? FailureReason);

/// <summary>
/// One abstraction for both money-collection gateways in the system: the platform-level inquiry
/// fee (PlatformPaymentSettings) and each agency's own down-payment gateway (OrgSettings) — two
/// bank accounts, two configs, one contract. Provider-agnostic by design (CLAUDE.md: no
/// hard-coded vendor); the implementation behind it does the vendor-specific work.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The provider this instance serves — the service layer matches it against the
    /// configured PaymentProvider before charging.</summary>
    PaymentProvider Provider { get; }

    /// <summary>Charges amount toman for one portal payment. Idempotent per paymentToken: a
    /// retried charge must not double-charge.</summary>
    Task<GatewayPaymentResult> ChargeAsync(
        string paymentToken,
        string merchantId,
        decimal amountToman,
        string description,
        string callbackUrl,
        CancellationToken ct = default);
}
