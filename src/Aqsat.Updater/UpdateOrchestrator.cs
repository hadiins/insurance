using System.Collections.Concurrent;
using Aqsat.Updater.Backup;
using Aqsat.Updater.Docker;
using Aqsat.Updater.Security;

namespace Aqsat.Updater;

/// <summary>
/// Sequences an update per docs/UPDATE-SYSTEM.md §3/§6: verify → backup → pull+verify digest →
/// recreate → health-check, with automatic rollback the moment anything after the backup fails.
/// The new container applies its own pending EF Core migrations on startup (src/Aqsat.Api/Program.cs,
/// Task 20's sp_getapplock-guarded MigrateAsync) — this service does not run migrations itself, it
/// only decides whether the container that just tried to comes back healthy.
///
/// Progress is in-memory only. This is a short-lived sidecar process, not the system of record —
/// Task 22's panel is expected to persist UpdateRun/UpdateStageLog rows in the main API's database
/// by polling GET /progress/{id} and relaying it over SignalR; that durability lives there, not here.
/// </summary>
public sealed class UpdateOrchestrator(
    IPackageSignatureVerifier signatureVerifier,
    IPreUpdateBackupService backupService,
    IContainerOrchestrator containerOrchestrator,
    ILogger<UpdateOrchestrator> logger,
    TimeSpan? healthCheckWindow = null,
    TimeSpan? healthCheckPollInterval = null)
{
    private const string ApiContainerName = "aqsat-api";
    private readonly TimeSpan _healthCheckWindow = healthCheckWindow ?? TimeSpan.FromMinutes(2);
    private readonly TimeSpan _healthCheckPollInterval = healthCheckPollInterval ?? TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<Guid, UpdateProgress> _runs = new();

    public UpdateProgress? GetProgress(Guid runId) => _runs.GetValueOrDefault(runId);

    /// <summary>docs/UPDATE-SYSTEM.md §6 "manual rollback" — swaps to an already-pulled previous
    /// image tag and health-checks it, no signature check needed (it already ran when that image
    /// was first deployed). Does NOT restore a database backup — per the same section's "important
    /// limitation," an image rollback that followed a destructive migration needs a human to make
    /// that call explicitly, this service will not silently do it for them.</summary>
    public (bool Accepted, Guid? RunId) StartRollback(string toImageTag, CancellationToken ct)
    {
        var runId = Guid.NewGuid();
        var progress = new UpdateProgress { RunId = runId, ToVersion = toImageTag };
        _runs[runId] = progress;

        _ = RunRollbackAsync(toImageTag, progress, ct);

        return (true, runId);
    }

    private async Task RunRollbackAsync(string toImageTag, UpdateProgress progress, CancellationToken ct)
    {
        try
        {
            progress.Stage = UpdateStage.RecreatingContainer;
            await containerOrchestrator.RecreateContainerAsync(ApiContainerName, toImageTag, ct);
            progress.CompleteStage(UpdateStage.RecreatingContainer);

            progress.Stage = UpdateStage.HealthChecking;
            var healthy = await WaitForHealthyAsync(ct);
            progress.Status = healthy ? UpdateRunStatus.Success : UpdateRunStatus.Failed;
            if (!healthy)
            {
                progress.ErrorMessage = "Rolled-back container failed to report healthy — manual intervention required.";
            }

            progress.Stage = UpdateStage.Done;
            progress.PercentComplete = 100;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Manual rollback to {ImageTag} failed", toImageTag);
            progress.Status = UpdateRunStatus.Failed;
            progress.ErrorMessage = ex.Message;
        }
        finally
        {
            progress.CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>Rejects immediately (never even queued) if the signature doesn't check out — the
    /// task's own check. Everything after that point runs in the background so the HTTP caller
    /// gets a run id back right away and polls GET /progress/{id}, matching the panel's "closing
    /// the browser mid-update must not stop it" requirement.</summary>
    public (bool Accepted, Guid? RunId, string? RejectionReason) Start(UpdateManifest manifest, CancellationToken ct)
    {
        if (!signatureVerifier.Verify(manifest))
        {
            logger.LogWarning("Rejected update to {Version}: invalid signature", manifest.Version);
            return (false, null, "امضای بستهٔ به‌روزرسانی نامعتبر است.");
        }

        var runId = Guid.NewGuid();
        var progress = new UpdateProgress { RunId = runId, ToVersion = manifest.Version };
        _runs[runId] = progress;

        _ = RunAsync(manifest, progress, ct);

        return (true, runId, null);
    }

    private async Task RunAsync(UpdateManifest manifest, UpdateProgress progress, CancellationToken ct)
    {
        string? previousImageTag = null;

        try
        {
            progress.Stage = UpdateStage.VerifyingSignature;
            progress.CompleteStage(UpdateStage.VerifyingSignature);

            progress.Stage = UpdateStage.BackingUpDatabase;
            await backupService.BackupAsync(ct);
            progress.CompleteStage(UpdateStage.BackingUpDatabase);

            progress.Stage = UpdateStage.PullingImage;
            var pulledDigest = await containerOrchestrator.PullImageAsync(manifest.ImageTag, ct);
            if (!DigestMatches(pulledDigest, manifest.Sha256))
            {
                throw new InvalidOperationException(
                    $"Pulled image digest ({pulledDigest}) does not match the manifest's signed sha256 ({manifest.Sha256}) — refusing to deploy it even though the manifest signature was valid.");
            }

            progress.CompleteStage(UpdateStage.PullingImage);

            progress.Stage = UpdateStage.RecreatingContainer;
            previousImageTag = await containerOrchestrator.RecreateContainerAsync(ApiContainerName, manifest.ImageTag, ct);
            progress.CompleteStage(UpdateStage.RecreatingContainer);

            progress.Stage = UpdateStage.HealthChecking;
            var healthy = await WaitForHealthyAsync(ct);
            if (!healthy)
            {
                throw new InvalidOperationException("New container failed to report healthy within the health-check window.");
            }

            progress.CompleteStage(UpdateStage.HealthChecking);
            progress.Stage = UpdateStage.Done;
            progress.Status = UpdateRunStatus.Success;
            progress.PercentComplete = 100;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Update to {Version} failed at stage {Stage}", manifest.Version, progress.Stage);
            progress.ErrorMessage = ex.Message;

            if (previousImageTag is not null)
            {
                // The container was already swapped when this failed (health check) — roll back
                // the image. A failure BEFORE the swap (backup, pull, digest mismatch) needs no
                // rollback at all: the previously-running container was never touched.
                try
                {
                    await containerOrchestrator.RecreateContainerAsync(ApiContainerName, previousImageTag, ct);
                    progress.Status = UpdateRunStatus.RolledBack;
                    progress.Stage = UpdateStage.RolledBack;
                }
                catch (Exception rollbackEx)
                {
                    logger.LogCritical(rollbackEx, "Rollback to {PreviousImageTag} ALSO failed after update to {Version} failed — manual intervention required", previousImageTag, manifest.Version);
                    progress.Status = UpdateRunStatus.Failed;
                    progress.ErrorMessage += $" Rollback also failed: {rollbackEx.Message}";
                }
            }
            else
            {
                progress.Status = UpdateRunStatus.Failed;
            }
        }
        finally
        {
            progress.CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task<bool> WaitForHealthyAsync(CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + _healthCheckWindow;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await containerOrchestrator.IsHealthyAsync(ApiContainerName, ct))
            {
                return true;
            }

            await Task.Delay(_healthCheckPollInterval, ct);
        }

        return false;
    }

    private static bool DigestMatches(string pulledDigest, string manifestSha256)
    {
        // Tolerates bare hex digests, "sha256:"-prefixed digests, and full "repo@sha256:…"
        // RepoDigest entries — only the digest itself is ever compared.
        static string Normalize(string value)
        {
            var at = value.LastIndexOf('@');
            if (at >= 0)
            {
                value = value[(at + 1)..];
            }

            return value.Replace("sha256:", string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(Normalize(pulledDigest), Normalize(manifestSha256), StringComparison.OrdinalIgnoreCase);
    }
}
