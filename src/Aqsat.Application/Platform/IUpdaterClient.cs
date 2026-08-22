namespace Aqsat.Application.Platform;

public sealed record UpdaterManifest(string Version, string ImageTag, string Sha256, string SignatureBase64);

public sealed record UpdaterStartResult(bool Accepted, Guid? RunId, string? RejectionReason);

public sealed record UpdaterProgress(
    Guid RunId, string ToVersion, string Stage, string Status, int PercentComplete,
    string? ErrorMessage, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);

/// <summary>
/// The only thing in Aqsat.Api that talks to Aqsat.Updater (docs/TASKS.md Task 21's isolated
/// service) — plain HTTP+JSON over the compose network, the shared token from
/// Updater:SharedToken/X-Updater-Token. No project reference either direction; these records exist
/// purely so this side of the wire has its own types, never Aqsat.Updater's.
/// </summary>
public interface IUpdaterClient
{
    Task<UpdaterStartResult> StartUpdateAsync(UpdaterManifest manifest, CancellationToken ct = default);
    Task<UpdaterStartResult> StartRollbackAsync(string toImageTag, CancellationToken ct = default);
    Task<UpdaterProgress?> GetProgressAsync(Guid runId, CancellationToken ct = default);
}
