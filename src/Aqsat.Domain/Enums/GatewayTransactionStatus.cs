namespace Aqsat.Domain.Enums;

/// <summary>
/// Lifecycle of one GatewayTransaction. A row is created Pending at the moment the PSP accepts the
/// payment request and is only ever moved to Verified after the server-side verify call confirmed
/// the money actually moved — never on the strength of the callback's own success flag, which is
/// attacker-controllable.
/// </summary>
public enum GatewayTransactionStatus : byte
{
    /// <summary>Request accepted by the PSP; the customer is (or is about to be) on its payment
    /// page. Not yet money.</summary>
    Pending = 1,

    /// <summary>Verify confirmed the payment. The business action has been (or is being) finalised
    /// in the same transaction — this is the only status that may produce a Payment row.</summary>
    Verified = 2,

    /// <summary>The PSP rejected the request, or verify reported a definite failure. Kept (never
    /// deleted) so a repeated callback re-reads the same decision instead of retrying forever.</summary>
    Failed = 3,

    /// <summary>The customer never completed the payment within the window. Expiry is applied
    /// lazily on callback/read, like portal invitations.</summary>
    Expired = 4,
}
