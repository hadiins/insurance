using System.Net.Http.Json;
using System.Text.Json;
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

    // Mirrors Aqsat.Updater's UpdateStage/UpdateRunStatus enum ORDER (deliberate duplication —
    // this project has no reference to Aqsat.Updater; the wire contract is duplicated by design).
    // Keep in sync if Aqsat.Updater's enums ever change: its UpdateProgress serializes the raw
    // enums, which System.Text.Json emits as plain numbers.
    private static readonly string[] KnownStages =
        ["VerifyingSignature", "BackingUpDatabase", "PullingImage", "RecreatingContainer", "HealthChecking", "Done", "RolledBack"];
    private static readonly string[] KnownStatuses = ["Running", "Success", "Failed", "RolledBack"];

    public async Task<UpdaterProgress?> GetProgressAsync(Guid runId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/progress/{runId}", ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        // Aqsat.Updater serializes Stage/Status as raw enum numbers while this side's contract (and
        // the SignalR payload the panel already consumes) carries their NAMES as strings — parse by
        // hand and map, so a numeric payload turns into the same strings the panel expects, and an
        // unrecognized value degrades to a visible placeholder instead of a JsonException killing
        // the panel's progress-polling loop (PlatformUpdatesController polls every 3s for 15 min).
        var body = await response.Content.ReadFromJsonAsync<ProgressBody>(cancellationToken: ct);
        if (body is null)
        {
            return null;
        }

        return new UpdaterProgress(
            body.RunId ?? runId,
            body.ToVersion ?? "",
            EnumName(body.Stage, KnownStages),
            EnumName(body.Status, KnownStatuses),
            body.PercentComplete ?? 0,
            body.ErrorMessage,
            body.StartedAt ?? DateTimeOffset.UtcNow,
            body.CompletedAt);
    }

    /// <summary>Accepts either the string form ("PullingImage") or the numeric enum form (2) the
    /// Updater actually sends; anything unrecognized maps to a stable placeholder so the UI renders
    /// something honest instead of crashing.</summary>
    private static string EnumName(JsonElement element, string[] knownNames) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "Unknown",
        JsonValueKind.Number when element.TryGetInt32(out var value) && value >= 0 && value < knownNames.Length => knownNames[value],
        JsonValueKind.Number => $"Unknown({element.GetInt32()})",
        _ => "Unknown",
    };

    /// <summary>Loose body shape: property names bind case-insensitively (web defaults) while the
    /// enum-ish fields stay raw JsonElement for the tolerant mapping above.</summary>
    private sealed record ProgressBody(
        Guid? RunId, string? ToVersion, JsonElement Stage, JsonElement Status, int? PercentComplete,
        string? ErrorMessage, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);

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
