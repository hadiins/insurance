using Aqsat.Api.Contracts;
using Aqsat.Api.Hubs;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Concurrency;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 11, Layer 3 (docs/CONCURRENCY.md §4). Acquiring is never a permission-gated
/// action by itself — the underlying write endpoint the lock protects already enforces its own
/// permission; a lock is a coordination courtesy, not the authorization boundary. Force-release is
/// the one path that IS gated, on Lock.ForceRelease, with its three non-negotiable requirements: a
/// reason of at least 10 characters, an immutable audit row, and instant notification to the
/// previous holder.
/// </summary>
[ApiController]
[Route("api/locks")]
[Authorize]
public sealed class LocksController(
    AppDbContext dbContext,
    ILockService lockService,
    ICurrentUserContext currentUser,
    IHubContext<PresenceHub> presenceHub) : ControllerBase
{
    [HttpPost("acquire")]
    public async Task<ActionResult<LockStatusDto>> Acquire(AcquireLockRequest request, CancellationToken ct)
    {
        if (request.DurationMinutes <= 0)
        {
            return ValidationProblem("مدت قفل باید مثبت باشد.");
        }

        var status = await lockService.AcquireOrRenewAsync(
            currentUser.ActiveOrganizationId, request.EntityType, request.EntityId,
            currentUser.UserId, currentUser.DisplayName, TimeSpan.FromMinutes(request.DurationMinutes), ct);

        return Ok(ToDto(status));
    }

    [HttpPost("release")]
    public async Task<IActionResult> Release(ReleaseLockRequest request, CancellationToken ct)
    {
        await lockService.ReleaseAsync(request.EntityType, request.EntityId, currentUser.UserId, ct);
        return NoContent();
    }

    [HttpGet("status")]
    public async Task<ActionResult<LockStatusDto?>> Status([FromQuery] string entityType, [FromQuery] Guid entityId, CancellationToken ct)
    {
        var status = await lockService.GetActiveAsync(entityType, entityId, ct);
        return Ok(status is null ? null : ToDto(status.Value));
    }

    [HttpPost("{id:guid}/force-release")]
    [Authorize(Policy = Permissions.LockForceRelease)]
    public async Task<IActionResult> ForceRelease(Guid id, ForceReleaseLockRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 10)
        {
            return ValidationProblem("دلیل آزادسازی الزامی است (حداقل ۱۰ کاراکتر).");
        }

        var result = await lockService.ForceReleaseAsync(id, currentUser.UserId, request.Reason.Trim(), ct);
        if (result is null)
        {
            return NotFound();
        }

        var policyId = await ResolvePolicyIdAsync(result.Value.EntityType, result.Value.EntityId, ct);

        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = result.Value.AgencyId,
            UserId = currentUser.UserId,
            UserDisplayName = currentUser.DisplayName,
            EntityType = result.Value.EntityType,
            EntityId = result.Value.EntityId,
            PolicyId = policyId,
            Action = AuditAction.LockForceReleased,
            Description = $"قفل {result.Value.PreviousHolderDisplayName} توسط {currentUser.DisplayName} آزاد شد. دلیل: {request.Reason.Trim()}",
            OccurredAt = DateTimeOffset.UtcNow,
            IpAddress = CurrentRequestContext.IpAddress,
        });
        await dbContext.SaveChangesAsync(ct);

        await presenceHub.Clients.User(result.Value.PreviousHolderUserId.ToString()).SendAsync(
            "LockRevoked",
            new
            {
                entityType = result.Value.EntityType,
                entityId = result.Value.EntityId,
                reason = request.Reason.Trim(),
                by = currentUser.DisplayName,
            },
            ct);

        return NoContent();
    }

    private async Task<Guid> ResolvePolicyIdAsync(string entityType, Guid entityId, CancellationToken ct) => entityType switch
    {
        nameof(Policy) => entityId,
        nameof(Installment) => await dbContext.Installments.AsNoTracking()
            .Where(i => i.Id == entityId).Select(i => i.PolicyId).FirstOrDefaultAsync(ct),
        nameof(Collateral) => await dbContext.Collaterals.AsNoTracking()
            .Where(c => c.Id == entityId).Select(c => c.PolicyId).FirstOrDefaultAsync(ct),
        _ => throw new InvalidOperationException($"Cannot resolve a PolicyId for lock entity type '{entityType}'."),
    };

    private static LockStatusDto ToDto(LockStatus status) =>
        new(status.AcquiredByMe, status.LockId, status.LockedByUserId, status.LockedByDisplayName, status.AcquiredAt, status.ExpiresAt);

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
