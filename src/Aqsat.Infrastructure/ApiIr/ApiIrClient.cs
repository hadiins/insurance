using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aqsat.Application.ApiIr;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Aqsat.Infrastructure.ApiIr;

/// <summary>
/// docs/PHASE-1-SPEC.md §6 + CLAUDE.md rules 14/18/26. Retry-with-backoff and the circuit breaker
/// are NOT implemented here — they are the standard resilience handler attached to this HttpClient
/// in DependencyInjection.cs, so a single policy applies to every call uniformly rather than each
/// method re-implementing its own.
/// </summary>
public sealed class ApiIrClient(HttpClient httpClient, AppDbContext dbContext, IMemoryCache cache, IOptions<ApiIrOptions> options)
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
        var (success, _) = await CallAsync<SendResponse>(
            "/api/sw1/SendSms", new { mobile, message = text }, "SendSms", SendSmsCost, isPaidEndpoint: true, agencyId, ct);
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
        var wasSandboxed = isPaidEndpoint && !options.Value.AllowPaidEndpoints;
        var actualEndpoint = wasSandboxed ? "/api/Sandbox/Echo" : endpoint;

        var success = false;
        TResponse? data = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, actualEndpoint)
            {
                Content = JsonContent.Create(body),
            };
            if (!string.IsNullOrEmpty(options.Value.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
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
                var envelope = await response.Content.ReadFromJsonAsync<ApiIrEnvelope<TResponse>>(EnvelopeJsonOptions, ct);
                if (envelope is { Success: true })
                {
                    success = true;
                    data = envelope.Data;
                }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
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

    private sealed record IsHolidayResponse(bool IsHoliday);
    private sealed record ShahkarLiteResponse(bool IsMatched);
    private sealed record ChequeColorResponse(string? Color);
    private sealed record SendResponse(string? MessageId);
}
