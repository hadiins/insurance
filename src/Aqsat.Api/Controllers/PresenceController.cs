using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Infrastructure.Concurrency;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aqsat.Api.Controllers;

/// <summary>
/// Backs the shell's status bar, which renders on every page — deliberately its own tiny endpoint so
/// the count never drags the heavier dashboard payload along with it. The live value also arrives
/// pushed over PresenceHub ("ActiveUsersChanged"); this is the first paint and the fallback.
/// </summary>
[ApiController]
[Route("api/presence")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class PresenceController(
    PresenceConnectionRegistry connectionRegistry,
    ICurrentUserContext currentUser) : ControllerBase
{
    /// <summary>Distinct users with the app open in the caller's active agency. The connection
    /// registry is keyed per process and per agency, so RLS is not the boundary here — the agency id
    /// comes from the caller's resolved scope, never from the request.</summary>
    [HttpGet("active-users")]
    public ActionResult<ActiveUsersDto> ActiveUsers() =>
        Ok(new ActiveUsersDto(connectionRegistry.DistinctAppUserCount(currentUser.ActiveOrganizationId)));
}

public sealed record ActiveUsersDto(int Count);
