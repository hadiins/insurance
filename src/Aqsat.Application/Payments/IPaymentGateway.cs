using Aqsat.Domain.Enums;

namespace Aqsat.Application.Payments;

/// <summary>Result of a VERIFIED payment — vendor details stay behind IPaymentGateway.
/// The three evidence fields are what the PSP returns at verify time and are persisted on the
/// GatewayTransaction, because they are the only way anyone can later reconcile a payment against
/// the PSP's own panel (or answer a customer asking "which card did I pay with?"). All optional:
/// Mock produces none, and a provider that does not report one leaves it null rather than
/// inventing a placeholder that would read as a fact.</summary>
public readonly record struct GatewayPaymentResult(
    bool Succeeded,
    decimal? PaidAmountToman,
    string? FailureReason,
    string? RefId = null,
    string? PaidCardMask = null,
    string? BuyerIp = null);

/// <summary>
/// What the service layer asks a gateway to collect. PaymentToken is the caller's own idempotency
/// key (an invitation token, or a link token plus installment id); it travels to the gateway as its
/// invoice reference so a retried request is recognisable on both sides. Amount is TOMAN — the
/// system's single internal unit (rule 19) — and every implementation converts at its own edge.
/// </summary>
public sealed record GatewayChargeRequest(
    string PaymentToken,
    string MerchantId,
    decimal AmountToman,
    string Description,
    string CallbackUrl,
    string? Mobile = null,
    string? FullName = null);

/// <summary>
/// The outcome of the FIRST phase. Two shapes are possible and the distinction is the whole reason
/// this record exists:
///
///  • <see cref="RequiresRedirect"/> false — a gateway that can charge inline (Mock). The money is
///    already accounted for and <see cref="SimulatedPaidAmountToman"/> carries it, so the caller
///    finalises exactly as it always did. This is what keeps a fresh install (and the whole existing
///    test suite) working with no credential at all.
///
///  • <see cref="RequiresRedirect"/> true — a real PSP. Nothing is settled yet: the caller must
///    persist a Pending GatewayTransaction keyed by <see cref="GatewayReference"/>, send the browser
///    to <see cref="RedirectUrl"/>, and let the PSP's callback drive the verify.
/// </summary>
public sealed record GatewayChargeRequestResult(
    bool Succeeded,
    bool RequiresRedirect,
    string? RedirectUrl,
    string? GatewayReference,
    decimal? SimulatedPaidAmountToman,
    string? FailureReason);

/// <summary>
/// A PSP's return-to-site parameters, normalised so the service layer never touches HTTP types: the
/// controller merges query string and form body into one dictionary and hands it over, and each
/// implementation knows its own field names (GooyaPay's PaymentStatus/Authority, ZarinPal's
/// Status/Authority). <see cref="SuccessFlagSet"/> is the PSP's own claim only — a hint that a
/// verify is worth attempting, never a statement that money moved.
/// </summary>
public sealed record GatewayCallbackData(bool SuccessFlagSet, string Reference, string? InvoiceId);

/// <summary>
/// One abstraction for both money-collection gateways in the system: the platform-level inquiry
/// fee (PlatformPaymentSettings) and each agency's own down-payment/installment gateway
/// (OrgSettings) — two bank accounts, two configs, one contract. Provider-agnostic by design
/// (CLAUDE.md: no hard-coded vendor); the implementation behind it does the vendor-specific work.
///
/// Two phases, because no real Iranian PSP can be charged in one call: the customer pays on the
/// PSP's own page and their browser comes back to us, so a request that cannot name where it will
/// return is not a request at all. Mock models this by resolving phase one immediately (see
/// <see cref="GatewayChargeRequestResult.RequiresRedirect"/>).
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The provider this instance serves — the service layer matches it against the
    /// configured PaymentProvider before charging.</summary>
    PaymentProvider Provider { get; }

    /// <summary>The smallest single payment this PSP accepts, in toman (گوياپی refuses below
    /// 1,000). Enforced by the caller BEFORE a request is sent, so an out-of-range amount produces
    /// a Persian explanation the operator can act on instead of an opaque PSP rejection.</summary>
    decimal MinAmountToman { get; }

    /// <summary>The largest single payment this PSP will accept, in toman (گوياپی 200,000,000;
    /// زرينپال 100,000,000 — they differ, which is exactly why this belongs to the provider and
    /// not to a shared constant). Enforced by the caller BEFORE a request is sent.
    /// Implementations that have no cap (Mock) report <see cref="decimal.MaxValue"/> and a
    /// <see cref="MinAmountToman"/> of 0.</summary>
    decimal MaxAmountToman { get; }

    /// <summary>Phase one — ask the PSP to start a payment. Idempotency is the caller's concern via
    /// <see cref="GatewayChargeRequest.PaymentToken"/>: a retried request must not create a second
    /// charge for the same intent.</summary>
    Task<GatewayChargeRequestResult> RequestAsync(GatewayChargeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Phase two — confirm with the PSP that the payment behind <paramref name="gatewayReference"/>
    /// actually happened, for exactly <paramref name="amountToman"/>, owned by
    /// <paramref name="merchantId"/>. This is the ONLY evidence that may produce a Payment row; the
    /// callback's own success flag is attacker-controllable and is never sufficient. Implementations
    /// must treat "already verified" as SUCCESS (ZarinPal's code 101) so a retried callback cannot
    /// turn a real payment into an error.
    /// </summary>
    Task<GatewayPaymentResult> VerifyAsync(
        string gatewayReference,
        string merchantId,
        decimal amountToman,
        CancellationToken ct = default);

    /// <summary>Reads a callback's parameters into the normalised shape above, using this
    /// provider's own field names and success-flag convention.</summary>
    GatewayCallbackData ReadCallback(IReadOnlyDictionary<string, string> parameters);
}
