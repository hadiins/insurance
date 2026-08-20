using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 6 — global reference data (docs/PHASE-1-SPEC.md §2.3), not RLS-scoped, backing
/// the issuance form's line dropdown (the first field, per spec §5).
/// </summary>
[ApiController]
[Route("api/insurance-lines")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class InsuranceLinesController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InsuranceLineDto>>> List(CancellationToken ct)
    {
        var lines = await dbContext.InsuranceLines
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.SortOrder)
            .Select(l => new InsuranceLineDto(l.Id, l.ParentId, l.Code, l.NameFa, l.RequiresVehicle, l.RequiresProperty, l.SortOrder))
            .ToListAsync(ct);

        return Ok(lines);
    }
}
