namespace Aqsat.Application.Platform;

/// <summary>
/// docs/UPDATE-SYSTEM.md §7 — pure state, checked by Aqsat.Api's maintenance middleware on every
/// request. The SignalR 60-second warning broadcast and the Hangfire job pause/resume live in
/// Aqsat.Api (they need IHubContext/JobStorage respectively) — this service only tracks whether the
/// gate is up, not how anyone found out about it.
/// </summary>
public interface IMaintenanceModeService
{
    bool IsActive { get; }
    DateTimeOffset? EstimatedEndsAt { get; }

    void Activate(TimeSpan estimatedDuration);
    void Deactivate();
}
