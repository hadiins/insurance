using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// لاگ فعالیت — per-record history (PoliciesController.File, CustomersController.File) is already a
/// query against this same table filtered by PolicyId (CLAUDE.md rule 28); this is the unfiltered
/// agency-wide view for oversight, gated behind Settings.Write like the rest of the settings group.
/// </summary>
[ApiController]
[Route("api/settings/audit-log")]
[Authorize(Policy = Permissions.SettingsWrite)]
public sealed class AuditLogController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuditLogRowDto>>> List(
        [FromQuery] string? search, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var query = dbContext.AuditEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a => a.UserDisplayName.Contains(term) || a.Description.Contains(term));
        }

        if (from is { } fromDate)
        {
            var start = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(a => a.OccurredAt >= start);
        }

        if (to is { } toDate)
        {
            var end = new DateTimeOffset(toDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            query = query.Where(a => a.OccurredAt <= end);
        }

        var rows = await query
            .OrderByDescending(a => a.OccurredAt)
            .Take(200)
            .Select(a => new AuditLogRowDto(a.Id, a.UserDisplayName, a.EntityType, a.Action.ToString(), a.Description, a.OccurredAt))
            .ToListAsync(ct);

        return Ok(rows);
    }
}
