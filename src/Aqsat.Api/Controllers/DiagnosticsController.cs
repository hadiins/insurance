using Aqsat.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aqsat.Api.Controllers;

/// <summary>
/// Exists only to make Task 4's own check exercisable over real HTTP before any real business
/// endpoint (Payments, Policies, ...) exists to gate on a permission. Later tasks' real endpoints
/// make this redundant.
/// </summary>
[ApiController]
[Route("api/_diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    [HttpGet("payment-write-probe")]
    [Authorize(Policy = Permissions.PaymentWrite)]
    public IActionResult PaymentWriteProbe() => Ok(new { ok = true });
}
