using Aqsat.Application.Payments;
using Aqsat.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Aqsat.Infrastructure.Payments;

/// <summary>
/// The Mock in PaymentProvider: a fully simulated gateway — ChargeAsync always succeeds at the
/// exact amount and no real money moves. Its whole point is that a fresh install (or this dev
/// environment) completes the entire portal payment flow before any PSP credential exists; the
/// confirm click on the portal's simulated gateway page is the "charge". Selecting it in
/// production is a configuration mistake the settings panel's own notes warn about.
/// </summary>
public sealed class MockPaymentGateway(ILogger<MockPaymentGateway> logger) : IPaymentGateway
{
    public PaymentProvider Provider => PaymentProvider.Mock;

    public Task<GatewayPaymentResult> ChargeAsync(
        string paymentToken,
        string merchantId,
        decimal amountToman,
        string description,
        string callbackUrl,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Mock gateway charged {Amount} toman (payment token {PaymentToken}, merchant '{MerchantId}', callback '{CallbackUrl}') — simulated, no real money moved.",
            amountToman, paymentToken, merchantId, callbackUrl);

        return Task.FromResult(new GatewayPaymentResult(true, amountToman, null));
    }
}
