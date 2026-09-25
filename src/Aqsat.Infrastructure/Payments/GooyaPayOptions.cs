namespace Aqsat.Infrastructure.Payments;

/// <summary>
/// Endpoint configuration for گویا پی, shaped like ApiIrOptions: one base URL plus the three paths,
/// so pointing at a GooyaPay test host later is a configuration change and never a code change.
/// Deliberately NO credential here — the merchant id is per-agency/per-owner data and lives in
/// OrgSettings.AgentMerchantId / PlatformPaymentSettings.OwnerMerchantId, where the settings panels
/// can mask and rotate it.
/// </summary>
public sealed class GooyaPayOptions
{
    public const string SectionName = "GooyaPay";

    /// <summary>GooyaPay has no sandbox environment (its documentation offers only the live host),
    /// which is why this gateway must be exercised with a real merchant id and a small amount —
    /// see docs/PAYMENT-GATEWAYS.md §4.</summary>
    public string BaseUrl { get; set; } = "https://gooyapay.ir";

    public string PaymentRequestPath { get; set; } = "/webservice/rest/PaymentRequest";

    public string PaymentVerificationPath { get; set; } = "/webservice/rest/PaymentVerification";

    /// <summary>Where the customer is sent to pay: {BaseUrl}{StartPayPath}/{Authority}. Only used
    /// when the request response omits its own PaymentUrl.</summary>
    public string StartPayPath { get; set; } = "/startPay";

    /// <summary>Request timeout in seconds. The PSP is an external dependency on the customer's
    /// critical path; a hung connection must fail into a retryable Persian message, not hold the
    /// request open.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
