using System.Collections.Concurrent;

namespace Aqsat.Infrastructure.Concurrency;

/// <summary>
/// SignalR's `OnDisconnectedAsync` gets only a ConnectionId, with no agency context — and RLS means
/// PresenceService cannot look up "which agency was this connection in" via a query, since it can't
/// read rows outside the (unknown) agency scope to find out. This in-memory map, populated on every
/// Enter/SetEditing, is what makes cleanup on disconnect possible. Single-process only — fine for
/// Phase 1 (no SignalR backplane/multi-instance scale-out yet).
/// </summary>
public sealed class PresenceConnectionRegistry
{
    private readonly ConcurrentDictionary<string, Guid> _agencyByConnectionId = new();

    public void Track(string connectionId, Guid agencyId) => _agencyByConnectionId[connectionId] = agencyId;

    public bool TryGetAgency(string connectionId, out Guid agencyId) => _agencyByConnectionId.TryGetValue(connectionId, out agencyId);

    public void Forget(string connectionId) => _agencyByConnectionId.TryRemove(connectionId, out _);
}
