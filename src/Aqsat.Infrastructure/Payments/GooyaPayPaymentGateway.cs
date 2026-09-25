using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aqsat.Application.Payments;
using Aqsat.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aqsat.Infrastructure.Payments;

/// <summary>
/// گویا پی behind IPaymentGateway (https://gooyapay.ir/public/document).
///
/// Shape of the integration:
///  • Authentication is the MerchantID inside the JSON body — there is no bearer token and no
///    request signature, so the merchant id is a credential and is never logged in clear.
///  • Amount is an integer in TOMAN, which is this system's own internal unit (rule 19), so no
///    currency conversion happens anywhere in this class. (زرینپال does need one — that asymmetry
///    is why the shared contract is denominated in toman.)
///  • The callback carries PaymentStatus=OK plus the Authority. That flag is the PSP's claim and
///    attacker-controllable, so it only decides whether a verify is worth attempting; the money is
///    only ever treated as received when <see cref="VerifyAsync"/> says so.
///  • There is no sandbox host, so this gateway can only be exercised against a live merchant id
///    (see docs/PAYMENT-GATEWAYS.md §4).
/// </summary>
public sealed class GooyaPayPaymentGateway(
    HttpClient httpClient,
    IOptions<GooyaPayOptions> options,
    ILogger<GooyaPayPaymentGateway> logger) : IPaymentGateway
{
    /// <summary>The callback's success flag — GooyaPay's documented literal.</summary>
    private const string CallbackSuccessFlag = "OK";

    /// <summary>GooyaPay's documented success code.</summary>
    private const int SuccessStatus = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly GooyaPayOptions _options = options.Value;

    public PaymentProvider Provider => PaymentProvider.GooyaPay;

    /// <summary>"حداقل مبلغ قابل پرداخت 1,000 تومان و حداکثر 200.000.000 تومان میباشد."</summary>
    public decimal MinAmountToman => 1_000m;

    public decimal MaxAmountToman => 200_000_000m;

    public async Task<GatewayChargeRequestResult> RequestAsync(
        GatewayChargeRequest request, CancellationToken ct = default)
    {
        if (!PspAmount.TryToWholeToman(request.AmountToman, out var amount)
            || amount < MinAmountToman || amount > MaxAmountToman)
        {
            return Failed(
                $"مبلغ قابل پرداخت در درگاه گویا پی باید بین {MinAmountToman:N0} و {MaxAmountToman:N0} تومان باشد.");
        }

        if (string.IsNullOrWhiteSpace(request.MerchantId))
        {
            return Failed("شناسهٔ پذیرندهٔ گویا پی ثبت نشده است؛ پرداخت ممکن نیست.");
        }

        if (string.IsNullOrWhiteSpace(request.CallbackUrl))
        {
            return Failed("آدرس بازگشت پرداخت تنظیم نشده است؛ پرداخت ممکن نیست.");
        }

        var body = new PaymentRequestDto(
            MerchantID: request.MerchantId.Trim(),
            Amount: amount,
            CallbackURL: request.CallbackUrl,
            // The caller's own idempotency key, echoed back on the callback — it is what lets a
            // returning visitor be matched to the exact charge they started.
            InvoiceID: request.PaymentToken,
            Description: Truncate(request.Description, 200),
            Mobile: Truncate(request.Mobile, 20),
            FullName: Truncate(request.FullName, 120));

        PaymentRequestResponseDto? response;
        try
        {
            using var httpResponse = await httpClient.PostAsJsonAsync(
                _options.PaymentRequestPath, body, JsonOptions, ct);
            response = await httpResponse.Content.ReadFromJsonAsync<PaymentRequestResponseDto>(JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Never an empty catch (rule 15): the customer gets a retryable explanation and the log
            // carries the cause.
            logger.LogWarning(ex, "GooyaPay payment request failed for invoice {InvoiceId}.", request.PaymentToken);
            return Failed("ارتباط با درگاه پرداخت گویا پی برقرار نشد. لطفاً دوباره تلاش کنید.");
        }

        if (response is null)
        {
            return Failed("پاسخ درگاه پرداخت گویا پی نامعتبر بود. لطفاً دوباره تلاش کنید.");
        }

        if (response.Status != SuccessStatus || string.IsNullOrWhiteSpace(response.Authority))
        {
            logger.LogWarning(
                "GooyaPay rejected the payment request for invoice {InvoiceId} with status {Status}.",
                request.PaymentToken, response.Status);
            return Failed(DescribeFailure(response.Status));
        }

        // PaymentForm is deliberately ignored: it is HTML supplied by the PSP, and rendering it
        // would be an injected-markup vector. Only the URL is used — the browser is sent there and
        // the PSP's own page takes over, which is also what its documentation describes.
        var redirectUrl = !string.IsNullOrWhiteSpace(response.PaymentUrl)
            ? response.PaymentUrl
            : $"{_options.BaseUrl.TrimEnd('/')}{_options.StartPayPath}/{response.Authority}";

        logger.LogInformation(
            "GooyaPay accepted the payment request for invoice {InvoiceId} ({Amount} toman).",
            request.PaymentToken, amount);

        return new GatewayChargeRequestResult(
            Succeeded: true,
            RequiresRedirect: true,
            RedirectUrl: redirectUrl,
            GatewayReference: response.Authority,
            SimulatedPaidAmountToman: null,
            FailureReason: null);
    }

    public async Task<GatewayPaymentResult> VerifyAsync(
        string gatewayReference, string merchantId, decimal amountToman, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gatewayReference) || string.IsNullOrWhiteSpace(merchantId))
        {
            return new GatewayPaymentResult(false, null, "اطلاعات تأیید پرداخت ناقص است.");
        }

        if (!PspAmount.TryToWholeToman(amountToman, out var amount))
        {
            return new GatewayPaymentResult(false, null, "مبلغ تأیید پرداخت نامعتبر است.");
        }

        var body = new PaymentVerificationDto(
            MerchantID: merchantId.Trim(),
            Authority: gatewayReference,
            // Must equal the amount the request was created with — the PSP rejects a mismatch, which
            // is exactly the check that stops a tampered callback from settling a larger debt.
            Amount: amount);

        PaymentVerificationResponseDto? response;
        try
        {
            using var httpResponse = await httpClient.PostAsJsonAsync(
                _options.PaymentVerificationPath, body, JsonOptions, ct);
            response = await httpResponse.Content.ReadFromJsonAsync<PaymentVerificationResponseDto>(JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "GooyaPay verify call failed for authority {Authority}.", gatewayReference);
            return new GatewayPaymentResult(
                false, null, "بررسی نهایی پرداخت با درگاه گویا پی انجام نشد. لطفاً چند دقیقه بعد دوباره تلاش کنید.");
        }

        if (response is null)
        {
            return new GatewayPaymentResult(false, null, "پاسخ تأیید درگاه گویا پی نامعتبر بود.");
        }

        if (response.Status != SuccessStatus)
        {
            logger.LogWarning(
                "GooyaPay reported a failed verification for authority {Authority} with status {Status}.",
                gatewayReference, response.Status);
            return new GatewayPaymentResult(false, null, DescribeFailure(response.Status));
        }

        // Trust the PSP's own reported amount when it sends one, but never a non-positive figure: a
        // zero would settle a debt for nothing (the same guard the callers already apply). The
        // RefID / masked card / buyer IP are carried back as evidence for the gateway transaction.
        var paid = response.Amount is > 0 ? response.Amount : amount;

        return new GatewayPaymentResult(true, paid, null,
            RefId: response.RefID,
            PaidCardMask: response.MaskCardNumber,
            BuyerIp: response.BuyerIP);
    }

    /// <summary>GooyaPay posts PaymentStatus/Authority/InvoiceID back to CallbackURL (it only
    /// switches to GET when the request asked for RequestMethod=GET, which this integration never
    /// does). Both spellings are accepted so a proxy that rewrites the body's casing cannot silently
    /// invalidate a real payment.</summary>
    public GatewayCallbackData ReadCallback(IReadOnlyDictionary<string, string> parameters)
    {
        var flag = Read(parameters, "PaymentStatus", "paymentstatus", "status");
        var reference = Read(parameters, "Authority", "authority") ?? string.Empty;
        var invoiceId = Read(parameters, "InvoiceID", "invoiceid");

        return new GatewayCallbackData(
            SuccessFlagSet: string.Equals(flag, CallbackSuccessFlag, StringComparison.OrdinalIgnoreCase),
            Reference: reference,
            InvoiceId: invoiceId);
    }

    private static string? Read(
        IReadOnlyDictionary<string, string> parameters, params string[] names)
    {
        foreach (var name in names)
        {
            if (parameters.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// GooyaPay documents its negative status codes as a table on its own site, which this codebase
    /// has NOT transcribed — so the numeric code is surfaced verbatim instead of a guessed meaning
    /// that could send an operator down the wrong path. Callers that need the exact per-code
    /// wording can extend this switch once a code's meaning has been confirmed.
    /// </summary>
    private static string DescribeFailure(int? status) => status switch
    {
        null => "پاسخ درگاه پرداخت گویا پی نامعتبر بود. لطفاً دوباره تلاش کنید.",
        _ => $"درگاه پرداخت گویا پی این تراکنش را نپذیرفت (کد {status}). لطفاً با پشتیبانی تماس بگیرید.",
    };

    private static GatewayChargeRequestResult Failed(string reason) =>
        new(Succeeded: false, RequiresRedirect: false, RedirectUrl: null,
            GatewayReference: null, SimulatedPaidAmountToman: null, FailureReason: reason);

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];

    // ---- Wire contracts. Field names are the PSP's own (including its RefID / BuyerIP casing), so
    // an integration break shows up as a failed unit test rather than a silently-null property. ----

    private sealed record PaymentRequestDto(
        [property: JsonPropertyName("MerchantID")] string MerchantID,
        [property: JsonPropertyName("Amount")] long Amount,
        [property: JsonPropertyName("CallbackURL")] string CallbackURL,
        [property: JsonPropertyName("InvoiceID")] string? InvoiceID,
        [property: JsonPropertyName("Description")] string? Description,
        [property: JsonPropertyName("Mobile")] string? Mobile,
        [property: JsonPropertyName("FullName")] string? FullName);

    /// <summary>PaymentForm is intentionally absent — only PaymentUrl is consumed (see RequestAsync).</summary>
    private sealed record PaymentRequestResponseDto(
        [property: JsonPropertyName("Status")] int? Status,
        [property: JsonPropertyName("Authority")] string? Authority,
        [property: JsonPropertyName("PaymentUrl")] string? PaymentUrl);

    private sealed record PaymentVerificationDto(
        [property: JsonPropertyName("MerchantID")] string MerchantID,
        [property: JsonPropertyName("Authority")] string Authority,
        [property: JsonPropertyName("Amount")] long Amount);

    private sealed record PaymentVerificationResponseDto(
        [property: JsonPropertyName("Status")] int? Status,
        [property: JsonPropertyName("RefID")]
        [property: JsonConverter(typeof(JsonStringOrNumberConverter))] string? RefID,
        [property: JsonPropertyName("Amount")] decimal? Amount,
        [property: JsonPropertyName("BuyerIP")] string? BuyerIP,
        [property: JsonPropertyName("MaskCardNumber")] string? MaskCardNumber);
}
