using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.SignalR;

namespace Aqsat.Api.Hubs;

/// <summary>
/// The default IUserIdProvider reads ClaimTypes.NameIdentifier, but Program.cs disables inbound
/// claim-type mapping (options.MapInboundClaims = false) so our JWT's "sub" claim stays literal —
/// required for `Clients.User(...)` targeting to work, e.g. the force-release notification.
/// </summary>
public sealed class SubClaimUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
}
