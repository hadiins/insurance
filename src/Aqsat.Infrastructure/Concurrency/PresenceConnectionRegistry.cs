using System.Collections.Concurrent;

namespace Aqsat.Infrastructure.Concurrency;

/// <summary>
/// SignalR's `OnDisconnectedAsync` gets only a ConnectionId, with no agency context — and RLS means
/// PresenceService cannot look up "which agency was this connection in" via a query, since it can't
/// read rows outside the (unknown) agency scope to find out. This in-memory map, populated on every
/// Enter/SetEditing, is what makes cleanup on disconnect possible. Single-process only — fine for
/// Phase 1 (no SignalR backplane/multi-instance scale-out yet).
///
/// <see cref="AppSession"/> is the second, coarser layer: a connection that merely has the app open,
/// with no record in view. Record presence is registered only when a user opens a specific record,
/// so without this a user sitting on the dashboard would be invisible and "how many users are
/// active" would have no honest answer.
/// </summary>
public sealed class PresenceConnectionRegistry
{
    public sealed record AppSession(Guid AgencyId, Guid UserId);

    private readonly ConcurrentDictionary<string, Guid> _agencyByConnectionId = new();
    private readonly ConcurrentDictionary<string, AppSession> _appSessionByConnectionId = new();

    public void Track(string connectionId, Guid agencyId) => _agencyByConnectionId[connectionId] = agencyId;

    public bool TryGetAgency(string connectionId, out Guid agencyId) => _agencyByConnectionId.TryGetValue(connectionId, out agencyId);

    public void Forget(string connectionId)
    {
        _agencyByConnectionId.TryRemove(connectionId, out _);
        ForgetApp(connectionId);
    }

    public void TrackApp(string connectionId, Guid agencyId, Guid userId) =>
        _appSessionByConnectionId[connectionId] = new AppSession(agencyId, userId);

    public bool TryGetAppSession(string connectionId, out AppSession session) =>
        _appSessionByConnectionId.TryGetValue(connectionId, out session!);

    /// <summary>Distinct users (not connections) with the app open in this agency — one user with
    /// three tabs is one user.</summary>
    public int DistinctAppUserCount(Guid agencyId) => _appSessionByConnectionId.Values
        .Where(s => s.AgencyId == agencyId)
        .Select(s => s.UserId)
        .Distinct()
        .Count();

    public void ForgetApp(string connectionId) => _appSessionByConnectionId.TryRemove(connectionId, out _);
}
