namespace Aqsat.Infrastructure.Payments;

/// <summary>
/// Endpoint configuration for زرینپال, shaped like ApiIrOptions. BaseUrl is the switch that reaches
/// ZarinPal's sandbox (its documentation exposes a separate test host), so the whole gateway can be
/// exercised end to end before a live merchant id is involved — unlike GooyaPay, which has none.
/// Deliberately NO credential here: merchant ids live in
/// OrgSettings.AgentMerchantId / PlatformPaymentSettings.OwnerMerchantId.
/// </summary>
public sealed class ZarinPalOptions
{
    public const string SectionName = "ZarinPal";

    /// <summary>Live host. Point this at ZarinPal's sandbox host to test without moving money.</summary>
    public string BaseUrl { get; set; } = "https://payment.zarinpal.com";

    public string RequestPath { get; set; } = "/pg/v4/payment/request.json";

    public string VerifyPath { get; set; } = "/pg/v4/payment/verify.json";

    /// <summary>Where the customer is sent to pay: {BaseUrl}{StartPayPath}/{authority}.</summary>
    public string StartPayPath { get; set; } = "/pg/StartPay";

    public int TimeoutSeconds { get; set; } = 30;
}
