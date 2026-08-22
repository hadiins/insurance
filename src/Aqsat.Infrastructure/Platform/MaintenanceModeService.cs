using Aqsat.Application.Platform;

namespace Aqsat.Infrastructure.Platform;

/// <summary>Singleton, in-memory — deliberately not persisted. A process restart clearing
/// maintenance mode is the correct behavior, not a bug: if the API restarted, whatever update was
/// in progress either finished or the container swap itself is what's happening right now, and a
/// stale "still in maintenance" flag surviving that restart would be actively wrong.</summary>
public sealed class MaintenanceModeService : IMaintenanceModeService
{
    private readonly object _lock = new();
    private DateTimeOffset? _estimatedEndsAt;

    public bool IsActive { get; private set; }
    public DateTimeOffset? EstimatedEndsAt => _estimatedEndsAt;

    public void Activate(TimeSpan estimatedDuration)
    {
        lock (_lock)
        {
            IsActive = true;
            _estimatedEndsAt = DateTimeOffset.UtcNow + estimatedDuration;
        }
    }

    public void Deactivate()
    {
        lock (_lock)
        {
            IsActive = false;
            _estimatedEndsAt = null;
        }
    }
}
