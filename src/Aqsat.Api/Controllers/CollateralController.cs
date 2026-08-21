using Aqsat.Api.Contracts;
using Aqsat.Application.ApiIr;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 18 — cheque / promissory-note tracking (agent-requested). Registration,
/// status lifecycle (Held → AtBank → Cleared/Bounced), an upcoming view, and the ChequeColor lookup
/// (cached 30 days — CLAUDE.md rule 26, already implemented in ApiIrClient since Task 14).
/// </summary>
[ApiController]
[Route("api/collateral")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class CollateralController(AppDbContext dbContext, IApiIrClient apiIrClient, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CollateralDto>>> List(
        [FromQuery] string? type, [FromQuery] string? status, [FromQuery] int? upcomingDays, CancellationToken ct)
    {
        var query = dbContext.Collaterals.AsNoTracking().Include(c => c.Policy).ThenInclude(p => p.Customer).AsQueryable();

        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<CollateralType>(type, ignoreCase: true, out var parsedType))
        {
            query = query.Where(c => c.Type == parsedType);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<CollateralStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(c => c.Status == parsedStatus);
        }

        if (upcomingDays is { } days)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var horizon = today.AddDays(days);
            query = query.Where(c => c.DueDate != null && c.DueDate >= today && c.DueDate <= horizon
                && c.Status != CollateralStatus.Cleared && c.Status != CollateralStatus.Bounced);
        }

        var items = await query
            .OrderBy(c => c.DueDate)
            .Select(c => ToDto(c))
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<ActionResult<CollateralDto>> Create(CreateCollateralRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<CollateralType>(request.Type, ignoreCase: true, out var type))
        {
            return ValidationProblem("نوع وثیقه نامعتبر است.");
        }

        if (request.Amount <= 0)
        {
            return ValidationProblem("مبلغ باید مثبت باشد.");
        }

        var policyExists = await dbContext.Policies.AsNoTracking().AnyAsync(p => p.Id == request.PolicyId, ct);
        if (!policyExists)
        {
            return ValidationProblem("بیمه‌نامه یافت نشد.");
        }

        if (type == CollateralType.ChequeSayadi && string.IsNullOrWhiteSpace(request.SayadId))
        {
            return ValidationProblem("شناسهٔ صیادی چک الزامی است.");
        }

        var collateral = new Collateral
        {
            AgencyId = currentUser.ActiveOrganizationId,
            PolicyId = request.PolicyId,
            Type = type,
            SayadId = request.SayadId?.Trim(),
            BankName = request.BankName?.Trim(),
            Amount = request.Amount,
            DueDate = request.DueDate,
            Status = CollateralStatus.Held,
        };
        dbContext.Collaterals.Add(collateral);
        await dbContext.SaveChangesAsync(ct);

        var saved = await dbContext.Collaterals.AsNoTracking()
            .Include(c => c.Policy).ThenInclude(p => p.Customer)
            .FirstAsync(c => c.Id == collateral.Id, ct);
        return Ok(ToDto(saved));
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<ActionResult<CollateralDto>> UpdateStatus(Guid id, UpdateCollateralStatusRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<CollateralStatus>(request.Status, ignoreCase: true, out var status))
        {
            return ValidationProblem("وضعیت نامعتبر است.");
        }

        var collateral = await dbContext.Collaterals.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (collateral is null)
        {
            return NotFound();
        }

        collateral.Status = status;
        await dbContext.SaveChangesAsync(ct);

        var saved = await dbContext.Collaterals.AsNoTracking()
            .Include(c => c.Policy).ThenInclude(p => p.Customer)
            .FirstAsync(c => c.Id == collateral.Id, ct);
        return Ok(ToDto(saved));
    }

    /// <summary>docs/PHASE-1-SPEC.md §6 — a real, costed api.ir call (150 toman) behind a manual
    /// button, never automatic — the agent decides when the color check is worth paying for.</summary>
    [HttpPost("{id:guid}/check-color")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<ActionResult<CollateralDto>> CheckColor(Guid id, CancellationToken ct)
    {
        var collateral = await dbContext.Collaterals.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (collateral is null)
        {
            return NotFound();
        }

        if (collateral.Type != CollateralType.ChequeSayadi || string.IsNullOrWhiteSpace(collateral.SayadId))
        {
            return ValidationProblem("استعلام رنگ چک فقط برای چک صیادی با شناسهٔ معتبر ممکن است.");
        }

        var color = await apiIrClient.ChequeColorAsync(collateral.SayadId, collateral.AgencyId, ct);
        collateral.ColorCode = color;
        collateral.CheckedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        var saved = await dbContext.Collaterals.AsNoTracking()
            .Include(c => c.Policy).ThenInclude(p => p.Customer)
            .FirstAsync(c => c.Id == collateral.Id, ct);
        return Ok(ToDto(saved));
    }

    private static CollateralDto ToDto(Collateral c) => new(
        c.Id, c.PolicyId, c.Policy.PolicyNumber, c.Policy.Customer.FullName, c.Type.ToString(),
        c.SayadId, c.BankName, c.Amount, c.DueDate, c.Status.ToString(), c.ColorCode, c.CheckedAt);

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
