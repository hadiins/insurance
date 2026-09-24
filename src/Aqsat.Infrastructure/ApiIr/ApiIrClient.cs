using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private const decimal SendSmsCost = 115m;
    private const decimal SmsOtpCost = 115m;
    private const decimal ChequeColorCost = 1_100m;

    // The one capability that is fully implemented and tested yet called by nobody (owner decision
    // 2026-09-23, docs/PHASE-1-SPEC.md §6): the registered IHolidayChecker is the Friday-only
    // WeekendOnlyHolidayChecker, so this 150-Toman lookup is never billed. The constant stays so the
    // call-log accounting is already correct the moment the lookup is switched back on.
    private const decimal IsHolidayCost = 150m;

    // api.ir owner-confirmed per-call prices (toman), 2026-09-23. These values are recorded in
    // ApiIrCallLog for every accepted call, including the explicit sandbox-echo acknowledgement,
    // so agency spend reporting remains complete before paid endpoints are enabled.
    private const decimal UnpaidChequeCost = 5_700m;
    private const decimal ActiveLoansCost = 6_100m;

    // api.ir's envelope keys are lowerCamelCase; the DTOs here are PascalCase for C# convention.
    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>PARKED — no caller by owner decision (docs/PHASE-1-SPEC.md §6); the registered
    /// IHolidayChecker is the Friday-only WeekendOnlyHolidayChecker, and the adapter that used to
    /// call this method is gone. Kept, tested and sandbox-routable so switching the paid lookup back
    /// on never needs a code change here.</summary>
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

    public async Task<bool> SmsOtpAsync(string mobile, string code, Guid agencyId, CancellationToken ct = default)
    {
        // api.ir's SmsOTP carries our own generated code (trust level 1) — unlike SendSms (bulk, trust
        // level 5), which the account may not yet be granted. The caller owns generating/verifying the
        // code; we only deliver it. The endpoint requires `code`, so it is part of the body.
        var (success, _) = await CallAsync<SendResponse>(
            "/api/sw1/SmsOTP", new { mobile, code }, "SmsOTP", SmsOtpCost, isPaidEndpoint: true, agencyId, ct);
        return success;
    }

    public async Task<UnpaidChequeResult?> UnpaidChequeAsync(string nationalCode, Guid agencyId, CancellationToken ct = default)
    {
        var (success, data) = await CallAsync<UnpaidChequeResponse>(
            "/api/sw1/UnpaidCheque", new { nationalCode }, "UnpaidCheque", UnpaidChequeCost,
            isPaidEndpoint: true, agencyId, ct);
        if (!success)
        {
            return null;
        }

        // A sandboxed 2xx has no Data at all — report "no real answer" without failing the flow.
        return data is null
            ? new UnpaidChequeResult(null, null, null)
            : new UnpaidChequeResult(data.Count, data.SumAmount, data.SumBouncedAmount);
    }

    public async Task<ActiveLoansResult?> ActiveLoansAsync(string nationalCode, Guid agencyId, CancellationToken ct = default)
    {
        var (success, data) = await CallAsync<ActiveLoansResponse>(
            "/api/sw1/ActiveLoans", new { nationalCode }, "ActiveLoans", ActiveLoansCost,
            isPaidEndpoint: true, agencyId, ct);
        if (!success)
        {
            return null;
        }

        if (data is null)
        {
            return new ActiveLoansResult(null, null, null, null, null, null, null);
        }

        // api.ir returns `info: null` for a person with no active facilities — count is still real.
        var info = data.Info;
        return new ActiveLoansResult(
            data.Count,
            info?.TotalAmount,
            info?.DebtTotalAmount,
            info?.PastExpiredTotalAmount,
            info?.DeferredTotalAmount,
            info?.SuspiciousTotalAmount,
            info?.Dishonored);
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
        var (_, allowPaid) = await ResolveSettingsAsync(ct);
        var apiKey = await ResolveApiKeyAsync(agencyId, ct);
        var wasSandboxed = isPaidEndpoint && !allowPaid;
        var actualEndpoint = wasSandboxed ? "/api/Sandbox/Echo" : endpoint;

        var success = false;
        TResponse? data = null;
        string? failureMessage = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, actualEndpoint)
            {
                // Sandbox/Echo's EchoReq rejects any body without its required `name` field
                // ("Name required"), so a sandboxed call echoes the original fields plus a name.
                Content = JsonContent.Create(wasSandboxed ? WithEchoName(body) : body),
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
            else
            {
                // A non-2xx previously vanished without a trace: success stayed false while the
                // reason (401 bad key, 403 service not granted, 429 throttle) lived only in the
                // response body — today's "accepted but never delivered" OTP hunt hit exactly
                // this gap. Drain the body (connection reuse) and log a truncated reason.
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning(
                    "api.ir {Service} returned non-success status {StatusCode}: {Body}",
                    service, (int)response.StatusCode,
                    errorBody.Length > 300 ? errorBody[..300] : errorBody);
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

        var logEntry = dbContext.ApiIrCallLogs.Add(new ApiIrCallLog
        {
            AgencyId = agencyId,
            Service = service,
            Success = success,
            CostToman = costToman,
            WasSandboxed = wasSandboxed,
            CalledAt = DateTimeOffset.UtcNow,
        });
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // The api.ir call itself already completed; this row is only cost/audit metadata.
            // A broken log write (RLS policy drift on a rebuilt database — error 33504, transient
            // DB blip) must never surface as a 500 to the caller: an anonymous OTP request has no
            // SESSION_CONTEXT AgencyId, so an exempt-but-still-blocked table rejects the insert
            // AFTER the SMS was actually sent. Detach so the poisoned entry cannot fail a later
            // SaveChanges in the same scope, and degrade loudly (rule 15: log, never swallow).
            logEntry.State = EntityState.Detached;
            logger.LogWarning(ex, "Could not write ApiIrCallLog for {Service} — cost accounting for this call is lost", service);
        }

        return (success, data);
    }

    /// <summary>Sandbox/Echo (EchoReq) requires a `name` property next to whatever the real
    /// endpoint's body is — this merges the original request fields with one.</summary>
    private static Dictionary<string, object?> WithEchoName(object body)
    {
        var echoed = new Dictionary<string, object?> { ["name"] = "Aqsat" };
        foreach (var prop in body.GetType().GetProperties())
            echoed[prop.Name] = prop.GetValue(body);
        return echoed;
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
            string.IsNullOrWhiteSpace(row?.ApiKey) ? options.Value.ApiKey : NormalizeApiKey(row!.ApiKey),
            row?.AllowPaidEndpoints ?? options.Value.AllowPaidEndpoints);
        cache.Set(SettingsCacheKey, resolved, SettingsCacheTtl);
        return resolved;
    }

    /// <summary>
    /// The agency's own SmsApiKey (OrgSettings) wins; the platform-level key is the fallback, so an
    /// agency without its own key sends exactly like before per-agency keys existed. Cached per
    /// agency for 15s alongside the platform settings for the same reasons (panel change visible in
    /// seconds, no per-call row read).
    /// </summary>
    private async Task<string> ResolveApiKeyAsync(Guid agencyId, CancellationToken ct)
    {
        var (platformKey, _) = await ResolveSettingsAsync(ct);
        if (agencyId == Guid.Empty)
        {
            return platformKey;
        }

        var cacheKey = $"apiir:agencykey:{agencyId}";
        if (cache.TryGetValue(cacheKey, out string? agencyKey) && agencyKey is not null)
        {
            return agencyKey;
        }

        string? key = null;
        try
        {
            key = await dbContext.OrgSettings.AsNoTracking()
                .Where(s => s.OrganizationId == agencyId)
                .Select(s => s.SmsApiKey)
                .FirstOrDefaultAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read OrgSettings.SmsApiKey for agency {AgencyId} — falling back to the platform key", agencyId);
        }

        var resolved = NormalizeApiKey(string.IsNullOrWhiteSpace(key) ? platformKey : key);
        cache.Set(cacheKey, resolved, SettingsCacheTtl);
        return resolved;
    }

    /// <summary>Pasting the whole Authorization header ("Bearer eyJ…") instead of the bare key is
    /// an easy panel mistake that api.ir answers with 401 — strip the scheme at every resolve so
    /// an already-stored bad value keeps working too.</summary>
    private static string NormalizeApiKey(string key)
    {
        var trimmed = key.Trim();
        return trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? trimmed["Bearer ".Length..].Trim()
            : trimmed;
    }

    private sealed record IsHolidayResponse(bool IsHoliday);
    private sealed record ChequeColorResponse(string? Color);
    private sealed record SendResponse(string? MessageId);

    // The OpenAPI spec types every numeric field as ["integer","string"] — api.ir may send amounts
    // as JSON strings, so the envelope reader allows numbers to come in from strings too.
    private sealed record UnpaidChequeResponse(int? Count, decimal? SumAmount, decimal? SumBouncedAmount);
    private sealed record ActiveLoansResponse(int? Count, ActiveLoansDetailsResponse? Info);
    private sealed record ActiveLoansDetailsResponse(
        decimal? TotalAmount,
        decimal? DebtTotalAmount,
        decimal? PastExpiredTotalAmount,
        decimal? DeferredTotalAmount,
        decimal? SuspiciousTotalAmount,
        decimal? Dishonored);
}
