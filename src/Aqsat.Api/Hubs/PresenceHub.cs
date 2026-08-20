using System.IdentityModel.Tokens.Jwt;
using Aqsat.Application.Concurrency;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Hubs;

/// <summary>
/// docs/CONCURRENCY.md §3 (Layer 2 — presence). Every method re-derives agency scope itself: unlike
/// an HTTP request, a Hub method invocation does not run through ScopeResolutionMiddleware (that
/// only fires once, on the original connect handshake), so AgencyContext/CurrentUserContext are not
/// ambient here — each call must verify membership and set AgencyContext.Current itself before
/// touching any RLS-scoped table.
/// </summary>
[Authorize]
public sealed class PresenceHub(AppDbContext dbContext, IPresenceService presenceService) : Hub
{
    public async Task Enter(Guid agencyId, string entityType, Guid entityId)
    {
        var (userId, displayName) = await ResolveCallerAsync(agencyId);
        var group = GroupName(entityType, entityId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);

        var list = await presenceService.UpsertAsync(agencyId, entityType, entityId, userId, displayName, Context.ConnectionId, isEditing: false);
        await Clients.Group(group).SendAsync("PresenceChanged", list);
    }

    public async Task Heartbeat(Guid agencyId, string entityType, Guid entityId)
    {
        var (userId, _) = await ResolveCallerAsync(agencyId);
        await presenceService.TouchAsync(entityType, entityId, userId);
    }

    public async Task SetEditing(Guid agencyId, string entityType, Guid entityId, bool isEditing)
    {
        var (userId, displayName) = await ResolveCallerAsync(agencyId);
        var group = GroupName(entityType, entityId);

        var list = await presenceService.UpsertAsync(agencyId, entityType, entityId, userId, displayName, Context.ConnectionId, isEditing);
        await Clients.Group(group).SendAsync("PresenceChanged", list);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var removed = await presenceService.RemoveByConnectionAsync(Context.ConnectionId);
        foreach (var removal in removed)
        {
            AgencyContext.Current = removal.AgencyId;
            var group = GroupName(removal.EntityType, removal.EntityId);
            var list = await presenceService.ListAsync(removal.EntityType, removal.EntityId);
            await Clients.Group(group).SendAsync("PresenceChanged", list);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task<(Guid UserId, string DisplayName)> ResolveCallerAsync(Guid agencyId)
    {
        var userId = Guid.Parse(Context.User!.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

        // UserOrgRoles/Users are identity tables exempted from RLS (Task 3) — safe to query before
        // AgencyContext is set, same reasoning ScopeResolutionMiddleware relies on for HTTP requests.
        var membership = await dbContext.UserOrgRoles
            .AsNoTracking()
            .AnyAsync(m => m.UserId == userId && m.OrganizationId == agencyId && !m.IsDeleted);
        if (!membership)
        {
            throw new HubException("این کاربر در این نمایندگی عضو نیست.");
        }

        var displayName = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync() ?? "کاربر";

        AgencyContext.Current = agencyId;
        return (userId, displayName);
    }

    private static string GroupName(string entityType, Guid entityId) => $"{entityType}:{entityId}";
}
