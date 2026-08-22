using Aqsat.Updater;
using Aqsat.Updater.Backup;
using Aqsat.Updater.Docker;
using Aqsat.Updater.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aqsat.UnitTests.Updater;

/// <summary>
/// Orchestration logic only — the actual Docker/SQL calls are covered by
/// DockerContainerOrchestrator/PreUpdateBackupService being thin, directly-reviewable wrappers
/// (Docker itself isn't available in this dev environment; see docs/RESTORE-RUNBOOK.md for how the
/// same constraint was handled for Task 20's backup job). What's tested here is the sequencing and
/// rollback decision docs/UPDATE-SYSTEM.md §6 describes: a failure before the container is ever
/// touched needs no rollback; a failure after it (health check) must revert the image.
/// </summary>
public class UpdateOrchestratorTests
{
    [Fact]
    public async Task A_clean_run_reaches_100_percent_and_succeeds()
    {
        var containers = new FakeContainerOrchestrator { HealthyAfterRecreate = true };
        var orchestrator = BuildOrchestrator(signatureValid: true, containers: containers);
        var manifest = new UpdateManifest("1.5.0", "registry.example.ir/aqsat-api:1.5.0", "digest-abc", "sig");

        var (accepted, runId, _) = orchestrator.Start(manifest, CancellationToken.None);
        Assert.True(accepted);

        var progress = await WaitForCompletionAsync(orchestrator, runId!.Value);

        Assert.Equal(UpdateRunStatus.Success, progress.Status);
        Assert.Equal(100, progress.PercentComplete);
        Assert.Equal(UpdateStage.Done, progress.Stage);
        Assert.True(containers.RecreateCalled);
    }

    [Fact]
    public void An_invalid_signature_is_rejected_before_anything_else_runs()
    {
        var containers = new FakeContainerOrchestrator { HealthyAfterRecreate = true };
        var orchestrator = BuildOrchestrator(signatureValid: false, containers: containers);
        var manifest = new UpdateManifest("1.5.0", "registry.example.ir/aqsat-api:1.5.0", "digest-abc", "bad-sig");

        var (accepted, runId, rejectionReason) = orchestrator.Start(manifest, CancellationToken.None);

        Assert.False(accepted);
        Assert.Null(runId);
        Assert.NotNull(rejectionReason);
        Assert.False(containers.RecreateCalled);
    }

    [Fact]
    public async Task A_digest_mismatch_after_pulling_fails_without_ever_touching_the_running_container()
    {
        var containers = new FakeContainerOrchestrator { HealthyAfterRecreate = true, PulledDigest = "digest-DIFFERENT" };
        var orchestrator = BuildOrchestrator(signatureValid: true, containers: containers);
        var manifest = new UpdateManifest("1.5.0", "registry.example.ir/aqsat-api:1.5.0", "digest-abc", "sig");

        var (accepted, runId, _) = orchestrator.Start(manifest, CancellationToken.None);
        Assert.True(accepted);

        var progress = await WaitForCompletionAsync(orchestrator, runId!.Value);

        Assert.Equal(UpdateRunStatus.Failed, progress.Status);
        Assert.False(containers.RecreateCalled);
        Assert.Contains("digest", progress.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_failed_health_check_after_the_swap_rolls_back_to_the_previous_image()
    {
        var containers = new FakeContainerOrchestrator { HealthyAfterRecreate = false, PreviousImageTag = "registry.example.ir/aqsat-api:1.4.2" };
        var orchestrator = BuildOrchestrator(signatureValid: true, containers: containers);
        var manifest = new UpdateManifest("1.5.0", "registry.example.ir/aqsat-api:1.5.0", "digest-abc", "sig");

        var (accepted, runId, _) = orchestrator.Start(manifest, CancellationToken.None);
        Assert.True(accepted);

        var progress = await WaitForCompletionAsync(orchestrator, runId!.Value);

        Assert.Equal(UpdateRunStatus.RolledBack, progress.Status);
        // Recreate was called twice: once to the new version, once back to the previous one.
        Assert.Equal(2, containers.RecreateCallCount);
        Assert.Equal("registry.example.ir/aqsat-api:1.4.2", containers.LastRecreatedImageTag);
    }

    [Fact]
    public async Task A_backup_failure_never_touches_the_container_at_all()
    {
        var containers = new FakeContainerOrchestrator { HealthyAfterRecreate = true };
        var orchestrator = BuildOrchestrator(signatureValid: true, containers: containers, backupThrows: true);
        var manifest = new UpdateManifest("1.5.0", "registry.example.ir/aqsat-api:1.5.0", "digest-abc", "sig");

        var (accepted, runId, _) = orchestrator.Start(manifest, CancellationToken.None);
        Assert.True(accepted);

        var progress = await WaitForCompletionAsync(orchestrator, runId!.Value);

        Assert.Equal(UpdateRunStatus.Failed, progress.Status);
        Assert.False(containers.RecreateCalled);
        Assert.Equal(0, containers.PullCallCount);
    }

    private static UpdateOrchestrator BuildOrchestrator(bool signatureValid, FakeContainerOrchestrator containers, bool backupThrows = false) =>
        new(new FakeSignatureVerifier(signatureValid), new FakeBackupService(backupThrows), containers, NullLogger<UpdateOrchestrator>.Instance,
            healthCheckWindow: TimeSpan.FromMilliseconds(200), healthCheckPollInterval: TimeSpan.FromMilliseconds(20));

    private static async Task<UpdateProgress> WaitForCompletionAsync(UpdateOrchestrator orchestrator, Guid runId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var progress = orchestrator.GetProgress(runId);
            if (progress is { CompletedAt: not null })
            {
                return progress;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Update run did not complete within the test's wait window.");
    }

    private sealed class FakeSignatureVerifier(bool valid) : IPackageSignatureVerifier
    {
        public bool Verify(UpdateManifest manifest) => valid;
    }

    private sealed class FakeBackupService(bool shouldThrow) : IPreUpdateBackupService
    {
        public Task<string> BackupAsync(CancellationToken ct) =>
            shouldThrow ? throw new InvalidOperationException("simulated backup failure") : Task.FromResult("/backup/fake.bak");
    }

    private sealed class FakeContainerOrchestrator : IContainerOrchestrator
    {
        public bool HealthyAfterRecreate { get; set; }
        public string PulledDigest { get; set; } = "digest-abc";
        public string PreviousImageTag { get; set; } = "registry.example.ir/aqsat-api:previous";

        public bool RecreateCalled => RecreateCallCount > 0;
        public int RecreateCallCount { get; private set; }
        public int PullCallCount { get; private set; }
        public string? LastRecreatedImageTag { get; private set; }

        public Task<string> PullImageAsync(string imageTag, CancellationToken ct)
        {
            PullCallCount++;
            return Task.FromResult(PulledDigest);
        }

        public Task<string> RecreateContainerAsync(string containerName, string imageTag, CancellationToken ct)
        {
            RecreateCallCount++;
            LastRecreatedImageTag = imageTag;
            return Task.FromResult(PreviousImageTag);
        }

        public Task<bool> IsHealthyAsync(string containerName, CancellationToken ct) => Task.FromResult(HealthyAfterRecreate);
    }
}
