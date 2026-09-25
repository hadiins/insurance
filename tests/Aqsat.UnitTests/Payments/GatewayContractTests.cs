using System.Net;
using System.Text;
using System.Text.Json;
using Aqsat.Application.Payments;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Payments;
using Aqsat.Infrastructure.Portal;
using Aqsat.UnitTests.DataModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aqsat.UnitTests.Payments;

/// <summary>Contract tests for the two real PSP wire formats (docs/PAYMENT-GATEWAYS.md): the
/// request body, the verify envelope and the callback field names are the three places where an
/// upstream change or a typo silently loses real money, so each is pinned here against a stubbed
/// HttpMessageHandler — no network. The coordinator cases prove the pre-flight bounds run before
/// any request leaves the process. DB-touching coordinator behaviour (persisting, superseding,
/// idempotent verify) is covered end-to-end by InstallmentPaymentLinkEndpointTests and
/// PortalInvitationEndpointTests.</summary>
public class GatewayContractTests
{
    private const string GooyaBase = "https://gooyapay.test";
    private const string ZarinBase = "https://zarinpal.test";

    private static GatewayChargeRequest Charge(decimal amount = 50_000m, string token = "token-1") =>
        new(token, "m-1", amount, "پرداخت آزمایشی",
            "https://api.test/api/portal/x/callback",
            Mobile: "09120000000", FullName: "کاربر تست");

    private sealed class StubHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string? PathAndQuery, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method, request.RequestUri?.PathAndQuery, body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (GooyaPayPaymentGateway Gateway, StubHttpMessageHandler Handler) CreateGooya(
        string responseJson)
    {
        var handler = new StubHttpMessageHandler(responseJson);
        var gateway = new GooyaPayPaymentGateway(
            new HttpClient(handler) { BaseAddress = new Uri(GooyaBase) },
            Options.Create(new GooyaPayOptions { BaseUrl = GooyaBase }),
            NullLogger<GooyaPayPaymentGateway>.Instance);
        return (gateway, handler);
    }

    private static (ZarinPalPaymentGateway Gateway, StubHttpMessageHandler Handler) CreateZarinpal(
        string responseJson)
    {
        var handler = new StubHttpMessageHandler(responseJson);
        var gateway = new ZarinPalPaymentGateway(
            new HttpClient(handler) { BaseAddress = new Uri(ZarinBase) },
            Options.Create(new ZarinPalOptions { BaseUrl = ZarinBase }),
            NullLogger<ZarinPalPaymentGateway>.Instance);
        return (gateway, handler);
    }

    // ---- گویا پی ----

    [Fact]
    public async Task Gooya_request_below_the_psp_floor_fails_without_an_http_call()
    {
        var (gateway, handler) = CreateGooya("{}");

        var result = await gateway.RequestAsync(Charge(999m));

        Assert.False(result.Succeeded);
        Assert.Contains("گویا پی", result.FailureReason!);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Gooya_request_with_a_fractional_toman_amount_is_refused()
    {
        // PspAmount: a fraction would be silently truncated by the PSP and capture less than owed.
        var (gateway, handler) = CreateGooya("{}");

        var result = await gateway.RequestAsync(Charge(1_000.5m));

        Assert.False(result.Succeeded);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Gooya_request_posts_the_documented_body_and_returns_the_authority()
    {
        var (gateway, handler) = CreateGooya(
            """{"Status":100,"Authority":"GOOYA-AUTH-7","PaymentUrl":"https://gooyapay.test/pay/GOOYA-AUTH-7"}""");

        var result = await gateway.RequestAsync(Charge());

        Assert.True(result.Succeeded);
        Assert.True(result.RequiresRedirect);
        Assert.Equal("https://gooyapay.test/pay/GOOYA-AUTH-7", result.RedirectUrl);
        Assert.Equal("GOOYA-AUTH-7", result.GatewayReference);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("/webservice/rest/PaymentRequest", call.PathAndQuery);

        using var body = JsonDocument.Parse(call.Body);
        var root = body.RootElement;
        Assert.Equal("m-1", root.GetProperty("MerchantID").GetString());
        Assert.Equal(50_000, root.GetProperty("Amount").GetInt64());
        Assert.Equal("https://api.test/api/portal/x/callback", root.GetProperty("CallbackURL").GetString());
        Assert.Equal("token-1", root.GetProperty("InvoiceID").GetString());
        // RequestMethod is deliberately never sent — it is what keeps the callback a POST, which
        // the controller's merged query+form reader depends on.
        Assert.False(root.TryGetProperty("RequestMethod", out _));
    }

    [Fact]
    public async Task Gooya_request_surfaces_a_rejection_with_the_psp_status_code()
    {
        var (gateway, _) = CreateGooya("""{"Status":-52}""");

        var result = await gateway.RequestAsync(Charge());

        Assert.False(result.Succeeded);
        Assert.Contains("نپذیرفت", result.FailureReason!);
        Assert.Contains("52", result.FailureReason!);
    }

    [Fact]
    public async Task Gooya_verify_success_carries_the_evidence_and_parses_a_numeric_refid()
    {
        var (gateway, handler) = CreateGooya(
            """{"Status":100,"RefID":987654,"Amount":50000,"BuyerIP":"203.0.113.9","MaskCardNumber":"627412-******-3496"}""");

        var result = await gateway.VerifyAsync("GOOYA-AUTH-7", "m-1", 50_000m);

        Assert.True(result.Succeeded);
        Assert.Equal(50_000m, result.PaidAmountToman);
        Assert.Equal("987654", result.RefId);
        Assert.Equal("627412-******-3496", result.PaidCardMask);
        Assert.Equal("203.0.113.9", result.BuyerIp);

        var call = Assert.Single(handler.Calls);
        Assert.Equal("/webservice/rest/PaymentVerification", call.PathAndQuery);
        using var body = JsonDocument.Parse(call.Body);
        Assert.Equal(50_000, body.RootElement.GetProperty("Amount").GetInt64());
    }

    [Fact]
    public async Task Gooya_verify_accepts_a_string_refid_and_falls_back_to_the_asked_amount()
    {
        var (gateway, _) = CreateGooya("""{"Status":100,"RefID":"ABCD-1234"}""");

        var result = await gateway.VerifyAsync("GOOYA-AUTH-7", "m-1", 50_000m);

        Assert.True(result.Succeeded);
        Assert.Equal("ABCD-1234", result.RefId);
        Assert.Equal(50_000m, result.PaidAmountToman);
        Assert.Null(result.BuyerIp);
    }

    [Fact]
    public async Task Gooya_verify_never_settles_for_zero_when_the_psp_reports_a_nonpositive_amount()
    {
        var (gateway, _) = CreateGooya("""{"Status":100,"Amount":0}""");

        var result = await gateway.VerifyAsync("GOOYA-AUTH-7", "m-1", 50_000m);

        Assert.True(result.Succeeded);
        Assert.Equal(50_000m, result.PaidAmountToman);
    }

    [Fact]
    public async Task Gooya_verify_failure_reports_the_psp_status_code()
    {
        var (gateway, _) = CreateGooya("""{"Status":-50}""");

        var result = await gateway.VerifyAsync("GOOYA-AUTH-7", "m-1", 50_000m);

        Assert.False(result.Succeeded);
        Assert.Contains("نپذیرفت", result.FailureReason!);
        Assert.Contains("50", result.FailureReason!);
    }

    [Fact]
    public void Gooya_callback_reads_lowercase_aliases_case_insensitively()
    {
        var (gateway, _) = CreateGooya("{}");
        var parameters = new Dictionary<string, string>
        {
            ["paymentstatus"] = "ok",
            ["authority"] = "GOOYA-AUTH-7",
            ["invoiceid"] = "token-1",
        };

        var data = gateway.ReadCallback(parameters);

        Assert.True(data.SuccessFlagSet);
        Assert.Equal("GOOYA-AUTH-7", data.Reference);
        Assert.Equal("token-1", data.InvoiceId);
    }

    [Fact]
    public void Gooya_callback_with_a_nok_status_is_not_a_success_hint()
    {
        var (gateway, _) = CreateGooya("{}");
        var parameters = new Dictionary<string, string>
        {
            ["PaymentStatus"] = "NOK",
            ["Authority"] = "GOOYA-AUTH-7",
        };

        var data = gateway.ReadCallback(parameters);

        Assert.False(data.SuccessFlagSet);
        Assert.Equal("GOOYA-AUTH-7", data.Reference);
    }

    // ---- زرینپال ----

    [Fact]
    public async Task Zarinpal_request_sends_explicit_toman_currency_and_builds_the_startpay_url()
    {
        var (gateway, handler) = CreateZarinpal(
            """{"data":{"code":100,"authority":"ZAR-AUTH-9"},"errors":[]}""");

        var result = await gateway.RequestAsync(Charge());

        Assert.True(result.Succeeded);
        Assert.True(result.RequiresRedirect);
        Assert.Equal($"{ZarinBase}/pg/StartPay/ZAR-AUTH-9", result.RedirectUrl);
        Assert.Equal("ZAR-AUTH-9", result.GatewayReference);

        var call = Assert.Single(handler.Calls);
        Assert.Equal("/pg/v4/payment/request.json", call.PathAndQuery);

        using var body = JsonDocument.Parse(call.Body);
        var root = body.RootElement;
        // Currency must be IRT explicitly: the verify table's amount is ریال, and omitting the
        // currency is a silent factor-of-ten bug (class remarks #1).
        Assert.Equal("IRT", root.GetProperty("currency").GetString());
        Assert.Equal(50_000, root.GetProperty("amount").GetInt64());
        Assert.Equal("m-1", root.GetProperty("merchant_id").GetString());
        Assert.Equal("token-1", root.GetProperty("metadata").GetProperty("order_id").GetString());
    }

    [Fact]
    public async Task Zarinpal_request_above_the_published_cap_is_refused_without_an_http_call()
    {
        var (gateway, handler) = CreateZarinpal("{}");

        var result = await gateway.RequestAsync(Charge(100_000_001m));

        Assert.False(result.Succeeded);
        Assert.Contains("زرینپال", result.FailureReason!);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Zarinpal_verify_code_100_settles_and_carries_the_evidence()
    {
        var (gateway, handler) = CreateZarinpal(
            """{"data":{"code":100,"ref_id":"REF-1","card_pan":"502229-******-5678","amount":77000},"errors":[]}""");

        var result = await gateway.VerifyAsync("ZAR-AUTH-9", "m-1", 77_000m);

        Assert.True(result.Succeeded);
        Assert.Equal(77_000m, result.PaidAmountToman);
        Assert.Equal("REF-1", result.RefId);
        Assert.Equal("502229-******-5678", result.PaidCardMask);
        Assert.Null(result.BuyerIp); // ZarinPal reports no buyer IP at verify time — never guessed.

        var call = Assert.Single(handler.Calls);
        Assert.Equal("/pg/v4/payment/verify.json", call.PathAndQuery);
        using var body = JsonDocument.Parse(call.Body);
        Assert.Equal("IRT", body.RootElement.GetProperty("currency").GetString());
        Assert.Equal("ZAR-AUTH-9", body.RootElement.GetProperty("authority").GetString());
    }

    [Fact]
    public async Task Zarinpal_verify_code_101_already_verified_counts_as_success()
    {
        // Rule 24: a retried callback must read as success, not strand a genuinely paid payment.
        var (gateway, _) = CreateZarinpal(
            """{"data":{"code":101,"ref_id":"REF-2"},"errors":[]}""");

        var result = await gateway.VerifyAsync("ZAR-AUTH-9", "m-1", 77_000m);

        Assert.True(result.Succeeded);
        Assert.Equal("REF-2", result.RefId);
        Assert.Equal(77_000m, result.PaidAmountToman);
    }

    [Fact]
    public async Task Zarinpal_failure_with_an_errors_object_uses_the_published_code_table()
    {
        var (gateway, _) = CreateZarinpal(
            """{"data":[],"errors":{"code":-50,"message":"amount mismatch"}}""");

        var result = await gateway.VerifyAsync("ZAR-AUTH-9", "m-1", 77_000m);

        Assert.False(result.Succeeded);
        Assert.Contains("مبلغ پرداختشده", result.FailureReason!);
    }

    [Fact]
    public async Task Zarinpal_failure_with_an_errors_array_reads_the_first_entry()
    {
        // Polymorphic envelope (class remarks #3): data is an ARRAY on failure.
        var (gateway, _) = CreateZarinpal(
            """{"data":[],"errors":[{"code":-51,"message":"failed"}]}""");

        var result = await gateway.VerifyAsync("ZAR-AUTH-9", "m-1", 77_000m);

        Assert.False(result.Succeeded);
        Assert.Contains("لغو شد", result.FailureReason!);
    }

    [Fact]
    public void Zarinpal_callback_ok_reads_the_authority_and_nok_is_not_a_success_hint()
    {
        var (gateway, _) = CreateZarinpal("{}");

        var ok = gateway.ReadCallback(new Dictionary<string, string>
        {
            ["Status"] = "OK",
            ["Authority"] = "ZAR-AUTH-9",
        });
        Assert.True(ok.SuccessFlagSet);
        Assert.Equal("ZAR-AUTH-9", ok.Reference);
        Assert.Null(ok.InvoiceId); // ZarinPal returns no invoice reference — not invented.

        var nok = gateway.ReadCallback(new Dictionary<string, string>
        {
            ["Status"] = "NOK",
            ["Authority"] = "ZAR-AUTH-9",
        });
        Assert.False(nok.SuccessFlagSet);
        Assert.Equal("ZAR-AUTH-9", nok.Reference);
    }

    // ---- coordinator pre-flight ----

    [Fact]
    public void Coordinator_resolve_rejects_a_provider_that_is_not_registered()
    {
        var (gooya, _) = CreateGooya("{}");
        using var db = TestDbContextFactory.Create();
        var coordinator = new GatewayPaymentCoordinator(
            db, new IPaymentGateway[] { gooya }, new ConfigurationBuilder().Build());

        // Only GooyaPay is registered in this coordinator, so any other defined provider must be
        // refused with the Persian "not supported" message rather than a NullReference later.
        var ex = Assert.Throws<PortalInvitationException>(
            () => coordinator.Resolve(PaymentProvider.Mock));

        Assert.Contains("پشتیبانی", ex.Message);
    }

    [Fact]
    public async Task Coordinator_refuses_an_out_of_range_amount_before_any_request_is_sent()
    {
        var (gooya, handler) = CreateGooya("{}");
        using var db = TestDbContextFactory.Create();
        var coordinator = new GatewayPaymentCoordinator(
            db, new IPaymentGateway[] { gooya }, new ConfigurationBuilder().Build());

        var ex = await Assert.ThrowsAsync<PortalInvitationException>(() => coordinator.ChargeAsync(
            new GatewayChargeContext(
                PaymentProvider.GooyaPay, GatewayPurpose.Installment, Guid.NewGuid()),
            new GatewayChargeRequest(
                "token-1", "m-1", 500m, "پرداخت آزمایشی", "https://api.test/api/portal/x/callback")));

        Assert.Contains("GooyaPay", ex.Message);
        Assert.Empty(handler.Calls);
    }
}