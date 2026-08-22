using System.Net.Http.Json;
using Aqsat.Application.Platform;
using Microsoft.Extensions.Configuration;

namespace Aqsat.Infrastructure.Platform;

public sealed class UpdaterClient : IUpdaterClient
{
    private readonly HttpClient _httpClient;

    public UpdaterClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        var token = configuration["Updater:SharedToken"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Updater-Token", token);
        }
    }

    public async Task<UpdaterStartResult> StartUpdateAsync(UpdaterManifest manifest, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/update", manifest, ct);
        return await ParseStartResultAsync(response, ct);
    }

    public async Task<UpdaterStartResult> StartRollbackAsync(string toImageTag, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/rollback", new { ToImageTag = toImageTag }, ct);
        return await ParseStartResultAsync(response, ct);
    }

    public async Task<UpdaterProgress?> GetProgressAsync(Guid runId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/progress/{runId}", ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<UpdaterProgress>(ct);
    }

    private static async Task<UpdaterStartResult> ParseStartResultAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadFromJsonAsync<AcceptedBody>(ct);
            return new UpdaterStartResult(true, body?.RunId, null);
        }

        var error = await response.Content.ReadFromJsonAsync<ErrorBody>(ct);
        return new UpdaterStartResult(false, null, error?.Error ?? "درخواست به سرویس به‌روزرسانی ناموفق بود.");
    }

    private sealed record AcceptedBody(Guid RunId);

    private sealed record ErrorBody(string? Error);
}
