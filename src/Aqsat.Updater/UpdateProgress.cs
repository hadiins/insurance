namespace Aqsat.Updater;

public enum UpdateStage
{
    VerifyingSignature,
    BackingUpDatabase,
    PullingImage,
    RecreatingContainer,
    HealthChecking,
    Done,
    RolledBack,
}

public enum UpdateRunStatus
{
    Running,
    Success,
    Failed,
    RolledBack,
}

/// <summary>docs/UPDATE-SYSTEM.md §3's weighted-stage progress bar. Task 22's panel is the thing
/// that turns this into a UI; this service only ever hands out plain data.</summary>
public sealed class UpdateProgress
{
    public required Guid RunId { get; init; }
    public required string ToVersion { get; init; }
    public UpdateStage Stage { get; set; }
    public UpdateRunStatus Status { get; set; } = UpdateRunStatus.Running;
    public int PercentComplete { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    private static readonly IReadOnlyDictionary<UpdateStage, int> StageWeights = new Dictionary<UpdateStage, int>
    {
        [UpdateStage.VerifyingSignature] = 5,
        [UpdateStage.BackingUpDatabase] = 20,
        [UpdateStage.PullingImage] = 20,
        [UpdateStage.RecreatingContainer] = 30,
        [UpdateStage.HealthChecking] = 25,
    };

    /// <summary>Marks a stage complete and recomputes the running percentage from the fixed weight
    /// table above — the UI never has to know the weights itself.</summary>
    public void CompleteStage(UpdateStage stage)
    {
        Stage = stage;
        PercentComplete = Math.Min(100, PercentComplete + StageWeights.GetValueOrDefault(stage, 0));
    }
}
