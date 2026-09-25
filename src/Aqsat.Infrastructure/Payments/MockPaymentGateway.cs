using Aqsat.Application.Payments;
using Aqsat.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Aqsat.Infrastructure.Payments;

/// <summary>
/// The Mock in PaymentProvider: a fully simulated gateway — RequestAsync always succeeds at the
/// exact amount and no real money moves. Its whole point is that a fresh install (or this dev
/// environment) completes the entire portal payment flow before any PSP credential exists, and the
/// confirm click on the portal's simulated gateway page is the "charge".
///
/// It models the two-phase contract by resolving phase one IMMEDIATELY: RequiresRedirect is false
/// and SimulatedPaidAmountToman carries the amount, so every caller takes the same finalise path it
/// always did and the portal needs no PSP account at all. That is what keeps the existing test suite
/// and a fresh install working with zero configuration. Selecting it in production is a
/// configuration mistake the settings panel's own notes warn about.
/// </summary>
public sealed class MockPaymentGateway(ILogger<MockPaymentGateway> logger) : IPaymentGateway
{
    public PaymentProvider Provider => PaymentProvider.Mock;

    /// <summary>No PSP, so no bounds at all — the caller's pre-flight check can never fire for Mock.</summary>
    public decimal MinAmountToman => 0m;
    public decimal MaxAmountToman => decimal.MaxValue;

    public Task<GatewayChargeRequestResult> RequestAsync(
        GatewayChargeRequest request, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Mock gateway charged {Amount} toman (payment token {PaymentToken}, merchant '{MerchantId}', callback '{CallbackUrl}') — simulated, no real money moved.",
            request.AmountToman, request.PaymentToken, request.MerchantId, request.CallbackUrl);

        return Task.FromResult(new GatewayChargeRequestResult(
            Succeeded: true,
            RequiresRedirect: false,
            RedirectUrl: null,
            GatewayReference: null,
            SimulatedPaidAmountToman: request.AmountToman,
            FailureReason: null));
    }

    /// <summary>Only reachable if a Mock charge were ever left Pending, which it cannot be — the
    /// inline branch above settles at once. Confirms, rather than pretends, so a stray call can
    /// never invent a failure.</summary>
    public Task<GatewayPaymentResult> VerifyAsync(
        string gatewayReference, string merchantId, decimal amountToman, CancellationToken ct = default)
        => Task.FromResult(new GatewayPaymentResult(true, amountToman, null));

    /// <summary>Unreachable for Mock (nothing ever redirects), so it reports the benign shape rather
    /// than throwing: a throw here would surface as a 500 on a code path no user can reach.</summary>
    public GatewayCallbackData ReadCallback(IReadOnlyDictionary<string, string> parameters)
        => new(SuccessFlagSet: true, Reference: string.Empty, InvoiceId: null);
}
