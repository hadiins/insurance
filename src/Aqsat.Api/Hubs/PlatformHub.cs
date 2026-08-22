using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Aqsat.Api.Hubs;

/// <summary>
/// docs/UPDATE-SYSTEM.md §5/§7 — two audiences, two groups. "AllUsers" gets the 60-second
/// maintenance warning (every active user across every agency needs to know to save their work,
/// not just the Platform.Owner who triggered the update). "PlatformOwners" additionally gets live
/// update progress — nobody outside that role should see internal version/stage detail, even
/// though the warning itself is harmless to broadcast wide.
/// </summary>
[Authorize]
public sealed class PlatformHub : Hub
{
    public const string AllUsersGroup = "platform:all-users";
    public const string PlatformOwnersGroup = "platform:owners";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, AllUsersGroup);

        if (Context.User?.HasClaim("permission", "Platform.Owner") == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, PlatformOwnersGroup);
        }

        await base.OnConnectedAsync();
    }
}
