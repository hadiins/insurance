using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aqsat.Application.Payments;
using Aqsat.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aqsat.Infrastructure.Payments;

/// <summary>
/// زرینپال (REST v4) behind IPaymentGateway — https://www.zarinpal.com/docs/paymentGateway
///
/// Three details of this API drive the implementation and are each a way to lose real money if
/// handled casually:
///
///  1. <b>Unit.</b> The request accepts a currency (IRR/IRT) while the verify table documents its
///     amount as ریال. This class therefore sends <c>currency = "IRT"</c> on BOTH calls, so the
///     shared in-toman contract (rule 19) maps across with no multiplication anywhere. Leaving the
///     currency out and assuming a default is a silent factor-of-ten bug.
///
///  2. <b>Already-verified is success.</b> A successful verify returns code 100; verifying the SAME
///     transaction again returns 101. 101 means the money moved and was already confirmed, so it is
///     treated as SUCCESS — the opposite would turn an ordinary retried callback into a failure and
///     strand a genuinely paid installment. This aligns with the system's own idempotency rule
///     (rule 24) rather than fighting it.
///
///  3. <b>Response shape is polymorphic.</b> On success <c>data</c> is an object and <c>errors</c>
///     is an empty array; on failure <c>data</c> is an empty ARRAY and <c>errors</c> is an object.
///     Binding either to a POCO throws JsonException on the other case — turning a declined payment
///     into an unhandled 500. Both are therefore read from a JsonDocument defensively.
/// </summary>
public sealed class ZarinPalPaymentGateway(
    HttpClient httpClient,
    IOptions<ZarinPalOptions> options,
    ILogger<ZarinPalPaymentGateway> logger) : IPaymentGateway
{
    private const int SuccessCode = 100;

    /// <summary>"تراکنش وریفای شده است. قبلاً verify انجام شده" — also a success (see class remarks).</summary>
    private const int AlreadyVerifiedCode = 101;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ZarinPalOptions _options = options.Value;

    public PaymentProvider Provider => PaymentProvider.ZarinPal;

    /// <summary>Not stated as a floor by the documentation; the same 1,000-toman floor گویا پی
    /// publishes is applied so both real PSPs behave identically to the operator.</summary>
    public decimal MinAmountToman => 1_000m;

    /// <summary>"حداکثر مبلغ پرداختی ۱۰۰ میلیون تومان است" — error code -41.</summary>
    public decimal MaxAmountToman => 100_000_000m;

    public async Task<GatewayChargeRequestResult> RequestAsync(
        GatewayChargeRequest request, CancellationToken ct = default)
    {
        if (!PspAmount.TryToWholeToman(request.AmountToman, out var amount)
            || amount < MinAmountToman || amount > MaxAmountToman)
        {
            return Failed(
                $"مبلغ قابل پرداخت در درگاه زرینپال باید بین {MinAmountToman:N0} و {MaxAmountToman:N0} تومان باشد.");
        }

        if (string.IsNullOrWhiteSpace(request.MerchantId))
        {
            return Failed("شناسهٔ پذیرندهٔ زرینپال ثبت نشده است؛ پرداخت ممکن نیست.");
        }

        if (string.IsNullOrWhiteSpace(request.CallbackUrl))
        {
            return Failed("آدرس بازگشت پرداخت تنظیم نشده است؛ پرداخت ممکن نیست.");
        }

        var body = new RequestDto(
            merchant_id: request.MerchantId.Trim(),
            amount: amount,
            currency: CurrencyToman,
            callback_url: request.CallbackUrl,
            description: Truncate(request.Description, 255) ?? "پرداخت",
            metadata: BuildMetadata(request));

        ZarinPalData? data;
        try
        {
            using var httpResponse = await httpClient.PostAsJsonAsync(
                _options.RequestPath, body, JsonOptions, ct);
            var raw = await httpResponse.Content.ReadAsStringAsync(ct);
            data = Parse(raw);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "ZarinPal payment request failed for order {OrderId}.", request.PaymentToken);
            return Failed("ارتباط با درگاه پرداخت زرینپال برقرار نشد. لطفاً دوباره تلاش کنید.");
        }

        if (data is not { } result)
        {
            return Failed("پاسخ درگاه پرداخت زرینپال نامعتبر بود. لطفاً دوباره تلاش کنید.");
        }

        if (result.Code != SuccessCode || string.IsNullOrWhiteSpace(result.Authority))
        {
            logger.LogWarning(
                "ZarinPal rejected the payment request for order {OrderId} with code {Code}.",
                request.PaymentToken, result.Code);
            return Failed(DescribeFailure(result));
        }

        var redirectUrl = $"{_options.BaseUrl.TrimEnd('/')}{_options.StartPayPath}/{result.Authority}";

        logger.LogInformation(
            "ZarinPal accepted the payment request for order {OrderId} ({Amount} toman).",
            request.PaymentToken, amount);

        return new GatewayChargeRequestResult(
            Succeeded: true,
            RequiresRedirect: true,
            RedirectUrl: redirectUrl,
            GatewayReference: result.Authority,
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

        // Must match the request's amount and currency exactly, or ZarinPal answers -50.
        var body = new VerifyDto(
            merchant_id: merchantId.Trim(),
            amount: amount,
            currency: CurrencyToman,
            authority: gatewayReference);

        ZarinPalData? data;
        try
        {
            using var httpResponse = await httpClient.PostAsJsonAsync(
                _options.VerifyPath, body, JsonOptions, ct);
            var raw = await httpResponse.Content.ReadAsStringAsync(ct);
            data = Parse(raw);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "ZarinPal verify call failed for authority {Authority}.", gatewayReference);
            return new GatewayPaymentResult(
                false, null, "بررسی نهایی پرداخت با درگاه زرینپال انجام نشد. لطفاً چند دقیقه بعد دوباره تلاش کنید.");
        }

        if (data is not { } result)
        {
            return new GatewayPaymentResult(false, null, "پاسخ تأیید درگاه زرینپال نامعتبر بود.");
        }

        if (result.Code is SuccessCode or AlreadyVerifiedCode)
        {
            // The PSP reports the captured amount itself; a non-positive figure is a protocol
            // violation, not an under-capture, so it falls back to the amount we asked for. ref_id
            // and the masked card are carried back as evidence for the gateway transaction;
            // ZarinPal reports no buyer IP at verify time, so that stays null rather than guessed.
            var paid = result.Amount is > 0 ? result.Amount : amount;
            return new GatewayPaymentResult(true, paid, null,
                RefId: result.RefId,
                PaidCardMask: result.CardPan,
                BuyerIp: null);
        }

        logger.LogWarning(
            "ZarinPal reported a failed verification for authority {Authority} with code {Code}.",
            gatewayReference, result.Code);

        return new GatewayPaymentResult(false, null, DescribeFailure(result));
    }

    /// <summary>ZarinPal returns the customer with the authority and a Status of OK/NOK in the
    /// QUERY STRING (unlike گویا پی's POSTed body), which is why the controller merges both sources
    /// before calling this.</summary>
    public GatewayCallbackData ReadCallback(IReadOnlyDictionary<string, string> parameters)
    {
        var flag = Read(parameters, "Status", "status");
        var reference = Read(parameters, "Authority", "authority") ?? string.Empty;

        return new GatewayCallbackData(
            SuccessFlagSet: string.Equals(flag, "OK", StringComparison.OrdinalIgnoreCase),
            Reference: reference,
            InvoiceId: null);
    }

    /// <summary>
    /// Reads the (polymorphic — see class remarks) envelope into a flat shape. <c>data</c> is only
    /// consulted when it really is an object; <c>errors</c> is consulted when it is an object OR a
    /// non-empty array. Returns null only when the body is not JSON at all, which the caller reports
    /// as an invalid response rather than a failed payment.
    /// </summary>
    private static ZarinPalData? Parse(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawBody);
        var root = document.RootElement;

        int? code = null;
        string? message = null;
        string? authority = null;
        string? refId = null;
        string? cardPan = null;
        decimal? amount = null;

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            code = ReadInt(data, "code");
            message = ReadString(data, "message");
            authority = ReadString(data, "authority");
            refId = ReadString(data, "ref_id");
            cardPan = ReadString(data, "card_pan");
            amount = ReadDecimal(data, "amount");
        }

        if (root.TryGetProperty("errors", out var errors))
        {
            var errorObject = errors.ValueKind switch
            {
                JsonValueKind.Object => errors,
                JsonValueKind.Array when errors.GetArrayLength() > 0 => errors[0],
                _ => default,
            };

            if (errorObject.ValueKind == JsonValueKind.Object)
            {
                code ??= ReadInt(errorObject, "code");
                message ??= ReadString(errorObject, "message");
            }
        }

        return new ZarinPalData(code, message, authority, refId, cardPan, amount);
    }

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var parsed) ? parsed : null;

    private static decimal? ReadDecimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var parsed) ? parsed : null;

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };
    }

    private static string? Read(IReadOnlyDictionary<string, string> parameters, params string[] names)
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

    /// <summary>Optional metadata; the order id is our own idempotency key so the PSP panel and this
    /// system can be reconciled by the same string.</summary>
    private static RequestMetadata? BuildMetadata(GatewayChargeRequest request) =>
        string.IsNullOrWhiteSpace(request.Mobile) && string.IsNullOrWhiteSpace(request.PaymentToken)
            ? null
            : new RequestMetadata(order_id: request.PaymentToken, mobile: request.Mobile);

    /// <summary>
    /// ZarinPal's published code table. The wording is intentionally the PSP's own meaning rather
    /// than an invented one, so an operator reading it can act (or hand it to ZarinPal support)
    /// without guessing which side is at fault.
    /// </summary>
    private static string DescribeFailure(ZarinPalData data) => data.Code switch
    {
        -9 => "اطلاعات ارسالی به درگاه نامعتبر است (شناسهٔ پذیرنده، آدرس بازگشت، توضیحات یا مبلغ).",
        -10 => "شناسهٔ پذیرنده یا آیپی سرور برای درگاه زرینپال معتبر نیست.",
        -11 => "شناسهٔ پذیرندهٔ زرینپال فعال نیست؛ با پشتیبانی تماس بگیرید.",
        -12 => "تلاش بیش از حد مجاز در بازهٔ زمانی کوتاه؛ کمی بعد دوباره تلاش کنید.",
        -13 => "محدودیت تعداد تراکنش درگاه؛ با پشتیبانی زرینپال تماس بگیرید.",
        -14 => "دامنهٔ آدرس بازگشت با دامنهٔ ثبتشدهٔ درگاه مغایرت دارد.",
        -15 or -16 or -17 => "درگاه پرداخت زرینپال در وضعیت غیرفعال است؛ با پشتیبانی تماس بگیرید.",
        -18 => "استفاده از این شناسهٔ درگاه روی این دامنه مجاز نیست.",
        -19 => "امکان ایجاد تراکنش برای این درگاه وجود ندارد؛ با پشتیبانی تماس بگیرید.",
        -40 => "پارامترهای اضافی درخواست نامعتبر است.",
        -41 => $"حداکثر مبلغ پرداختی در درگاه زرینپال {100_000_000:N0} تومان است.",
        -50 => "مبلغ پرداختشده با مبلغ درخواست یکسان نیست.",
        -51 => "پرداخت ناموفق بود یا توسط پرداختکننده لغو شد.",
        -52 => "خطای غیرمنتظرهای در درگاه رخ داد؛ با پشتیبانی زرینپال تماس بگیرید.",
        -53 => "این پرداخت به شناسهٔ پذیرندهٔ فعلی تعلق ندارد.",
        -54 => "شناسهٔ مرجع پرداخت نامعتبر است.",
        -55 => "تراکنش مورد نظر در درگاه یافت نشد.",
        null => "درگاه پرداخت زرینپال پاسخ نامعتبری برگرداند.",
        _ => $"درگاه پرداخت زرینپال این تراکنش را نپذیرفت (کد {data.Code}). لطفاً با پشتیبانی تماس بگیرید.",
    };

    private static GatewayChargeRequestResult Failed(string reason) =>
        new(Succeeded: false, RequiresRedirect: false, RedirectUrl: null,
            GatewayReference: null, SimulatedPaidAmountToman: null, FailureReason: reason);

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];

    /// <summary>Explicit تومان on every amount, both calls (see class remarks #1).</summary>
    private const string CurrencyToman = "IRT";

    /// <summary>Flattened view of either envelope (data on success, errors on failure).</summary>
    private readonly record struct ZarinPalData(
        int? Code, string? Message, string? Authority, string? RefId, string? CardPan, decimal? Amount);

    private sealed record RequestDto(
        string merchant_id,
        long amount,
        string currency,
        string callback_url,
        string description,
        RequestMetadata? metadata);

    private sealed record RequestMetadata(string order_id, string? mobile);

    private sealed record VerifyDto(
        string merchant_id,
        long amount,
        string currency,
        string authority);
}
