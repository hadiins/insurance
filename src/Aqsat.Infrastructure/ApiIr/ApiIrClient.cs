using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aqsat.Application.ApiIr;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.ApiIr;

/// <summary>
/// docs/PHASE-1-SPEC.md §6 + CLAUDE.md rules 14/18/26. Retry-with-backoff and the circuit breaker
/// are NOT implemented here — they are the standard resilience handler attached to this HttpClient
/// in DependencyInjection.cs, so a single policy applies to every call uniformly rather than each
/// method re-implementing its own.
/// </summary>
public sealed class ApiIrClient(HttpClient httpClient, AppDbContext dbContext, IMemoryCache cache, IOptions<ApiIrOptions> options, ILogger<ApiIrClient> logger)
    : IApiIrClient
{
    private const decimal ShahkarCost = 550m;
    private const decimal SendSmsCost = 115m;
    private const decimal SmsOtpCost = 115m;
    private const decimal IsHolidayCost = 150m;
    private const decimal ChequeColorCost = 1_100m;
    private const decimal CallOtpCost = 95m;

    // api.ir's envelope keys are lowerCamelCase; the DTOs here are PascalCase for C# convention.
    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool?> IsHolidayAsync(DateOnly date, Guid agencyId, CancellationToken ct = default)
    {
        var cacheKey = $"apiir:isholiday:{date:yyyy-MM-dd}";
        if (cache.TryGetValue(cacheKey, out bool? cached))
        {
            return cached;
        }

        var (success, result) = await CallAsync<IsHolidayResponse>(
            "/api/sw1/IsHoliday", new { date = date.ToString("yyyy-MM-dd") }, "IsHoliday", IsHolidayCost, isPaidEndpoint: true, agencyId, ct);
        // A real (non-sandboxed) answer always carries Data; a sandboxed 2xx has none — both count
        // as "no real answer" for the caller's fallback decision.
        bool? isHoliday = success && result is not null ? result.IsHoliday : null;

        // "Cached to midnight" (rule 26) — expire at the next local midnight, not a fixed TTL. Even
        // a null (no real answer) is worth caching so a sandboxed day doesn't re-hit the network on
        // every single call for the same date.
        var midnight = date.ToDateTime(TimeOnly.MinValue).AddDays(1);
        cache.Set(cacheKey, isHoliday, new DateTimeOffset(midnight, TimeSpan.Zero));
        return isHoliday;
    }

    public async Task<ShahkarResult?> ShahkarLiteAsync(string nationalId, string mobile, Guid agencyId, CancellationToken ct = default)
    {
        var cacheKey = $"apiir:shahkar:{nationalId}:{mobile}";
        if (cache.TryGetValue(cacheKey, out ShahkarResult cached))
        {
            return cached;
        }

        var (shahkarSuccess, result) = await CallAsync<ShahkarLiteResponse>(
            "/api/sw1/ShahkarLite", new { mobile, nationalCode = nationalId }, "ShahkarLite", ShahkarCost,
            isPaidEndpoint: true, agencyId, ct);
        if (!shahkarSuccess || result is null)
        {
            return null;
        }

        var shahkar = new ShahkarResult(result.IsMatched);
        cache.Set(cacheKey, shahkar); // forever (rule 26) — no absolute/sliding expiration.
        return shahkar;
    }

    public async Task<string?> ChequeColorAsync(string sayadId, Guid agencyId, CancellationToken ct = default)
    {
        var cacheKey = $"apiir:chequecolor:{sayadId}";
        if (cache.TryGetValue(cacheKey, out string? cached))
        {
            return cached;
        }

        var (_, result) = await CallAsync<ChequeColorResponse>(
            "/api/sw1/ChequeColor", new { sayadId }, "ChequeColor", ChequeColorCost, isPaidEndpoint: true, agencyId, ct);
        var color = result?.Color;
        if (color is not null)
        {
            cache.Set(cacheKey, color, TimeSpan.FromDays(30));
        }

        return color;
    }

    public async Task<bool> SendSmsAsync(string mobile, string text, Guid agencyId, CancellationToken ct = default)
    {
        // api.ir's SendSms takes `mobiles` (an ARRAY) + `message` — the bare `mobile` string this
        // used to send is ignored server-side, so the send silently no-opped while the envelope
        // still reported success, and the panel showed an OTP code that never arrived anywhere.
        var (success, _) = await CallAsync<SendResponse>(
            "/api/sw1/SendSms", new { message = text, mobiles = new[] { mobile } }, "SendSms", SendSmsCost,
            isPaidEndpoint: true, agencyId, ct);
        return success;
    }

    public async Task<bool> SmsOtpAsync(string mobile, Guid agencyId, CancellationToken ct = default)
    {
        var (success, _) = await CallAsync<SendResponse>(
            "/api/sw1/SmsOTP", new { mobile }, "SmsOTP", SmsOtpCost, isPaidEndpoint: true, agencyId, ct);
        return success;
    }

    public async Task<bool> CallOtpAsync(string mobile, Guid agencyId, CancellationToken ct = default)
    {
        var (success, _) = await CallAsync<SendResponse>(
            "/api/sw1/CallOTP", new { mobile }, "CallOTP", CallOtpCost, isPaidEndpoint: true, agencyId, ct);
        return success;
    }

    /// <summary>
    /// `Success` means "the call was accepted" — true for a real call with `success:true` in the
    /// envelope (rule 18), and also true for a sandboxed call that got a 2xx from Sandbox/Echo (a
    /// send action still "worked" in sandbox mode; there is just no real envelope to parse `Data`
    /// from, so `Data` stays null for every sandboxed call). The resilience handler already retried
    /// transient failures before this method ever sees them; an api.ir outage still degrades to
    /// `(false, null)` here rather than throwing (docs/TASKS.md Task 12's own check).
    /// </summary>
    private async Task<(bool Success, TResponse? Data)> CallAsync<TResponse>(
        string endpoint, object body, string service, decimal costToman, bool isPaidEndpoint, Guid agencyId, CancellationToken ct)
        where TResponse : class
    {
        var (apiKey, allowPaid) = await ResolveSettingsAsync(ct);
        var wasSandboxed = isPaidEndpoint && !allowPaid;
        var actualEndpoint = wasSandboxed ? "/api/Sandbox/Echo" : endpoint;

        var success = false;
        TResponse? data = null;
        string? failureMessage = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, actualEndpoint)
            {
                Content = JsonContent.Create(body),
            };
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await httpClient.SendAsync(request, ct);
            if (wasSandboxed)
            {
                // Sandbox/Echo just reflects the request — there is no real envelope to check
                // `success` on, so a 2xx from Echo itself is the only signal available.
                success = response.IsSuccessStatusCode;
            }
            else if (response.IsSuccessStatusCode)
            {
                (success, data, failureMessage) = await ParseEnvelopeAsync<TResponse>(response, ct);
                if (!success && failureMessage is not null)
                {
                    // api.ir puts the *reason* (insufficient credit, wrong key, …) in `message` —
                    // without surfacing it, every rejection looks identical from the call logs.
                    logger.LogWarning("api.ir {Service} rejected the call: {Message}", service, failureMessage);
                }
            }
        }
        catch (Exception ex) when (ct.IsCancellationRequested is false)
        {
            // The failure still lands in ApiIrCallLogs as Success=false, but without this the
            // actual cause (DNS, TLS, 5xx body, timeout) was swallowed silently — a provider
            // outage or a misconfigured key would be indistinguishable from "no data".
            logger.LogWarning(ex, "api.ir call to {Service} failed", service);
            success = false;
        }

        dbContext.ApiIrCallLogs.Add(new ApiIrCallLog
        {
            AgencyId = agencyId,
            Service = service,
            Success = success,
            CostToman = costToman,
            WasSandboxed = wasSandboxed,
            CalledAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync(ct);

        return (success, data);
    }

    /// <summary>
    /// api.ir's real payloads only loosely match ApiIrEnvelope&lt;T&gt;: `data` is sometimes a bare
    /// number (SendSms answers with the numeric message id), and their docs' own warning example
    /// ships `success:false` WITH populated `data` — so the envelope is parsed by hand instead of
    /// bound to one rigid shape: `success` decides the outcome, `data` deserializes only when it is
    /// actually an object (a scalar id is not needed by any caller here — the bool is the answer),
    /// and `message` carries the provider's own failure reason up into the logs.
    /// </summary>
    private static async Task<(bool Success, TResponse? Data, string? Message)> ParseEnvelopeAsync<TResponse>(
        HttpResponseMessage response, CancellationToken ct)
        where TResponse : class
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;

        bool success;
        if (root.TryGetProperty("success", out var successEl) && successEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            success = successEl.GetBoolean();
        }
        else if (root.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var code))
        {
            // No explicit flag — fall back to api.ir's own numeric status code.
            success = code is >= 200 and < 300;
        }
        else
        {
            success = false;
        }

        var message = root.TryGetProperty("message", out var messageEl) && messageEl.ValueKind == JsonValueKind.String
            ? messageEl.GetString()
            : null;

        TResponse? data = null;
        if (success && root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
        {
            data = dataEl.Deserialize<TResponse>(EnvelopeJsonOptions);
        }

        return (success, data, message);
    }

    private const string SettingsCacheKey = "apiir:settings";
    private static readonly TimeSpan SettingsCacheTtl = TimeSpan.FromSeconds(15);

    private sealed record ResolvedSettings(string ApiKey, bool AllowPaidEndpoints);

    /// <summary>
    /// Panel-editable settings (ApiIrSettings) win; configuration (ApiIrOptions) is the fallback for
    /// every field the row leaves unset, so a fresh install behaves exactly as before the table
    /// existed. Cached for 15s: a panel change takes effect within seconds, while bursts of
    /// individually-priced calls never re-query the row. A DB failure here degrades to
    /// configuration and is logged — CallAsync's own SaveChangesAsync would fail on the same
    /// outage anyway, but sandbox/real routing must never silently flip either direction.
    /// </summary>
    private async Task<ResolvedSettings> ResolveSettingsAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(SettingsCacheKey, out object? cachedObj) && cachedObj is ResolvedSettings cached)
        {
            return cached;
        }

        ApiIrSettings? row = null;
        try
        {
            row = await dbContext.ApiIrSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read ApiIrSettings — falling back to configured ApiIr options");
        }

        var resolved = new ResolvedSettings(
            string.IsNullOrWhiteSpace(row?.ApiKey) ? options.Value.ApiKey : row!.ApiKey.Trim(),
            row?.AllowPaidEndpoints ?? options.Value.AllowPaidEndpoints);
        cache.Set(SettingsCacheKey, resolved, SettingsCacheTtl);
        return resolved;
    }

    private sealed record IsHolidayResponse(bool IsHoliday);
    private sealed record ShahkarLiteResponse(bool IsMatched);
    private sealed record ChequeColorResponse(string? Color);
    private sealed record SendResponse(string? MessageId);
}
