using System.Data;
using System.Security.Cryptography;
using System.Text;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// One-time setup for the actual software owner — docs/UPDATE-SYSTEM.md rule 1: "belongs to you,
/// not any agency." Unauthenticated by necessity (no owner account exists yet to authenticate
/// against), gated instead by a deploy-time shared secret and a "does an owner already exist" guard
/// that makes it permanently a no-op once used — this is a bootstrap, not a general-purpose
/// "create more owners" endpoint. The whole creation runs in one serializable transaction so two
/// concurrent attempts can't both pass the exists-check, and a mid-flight crash leaves nothing
/// behind (no half-created owner-less user).
/// </summary>
[ApiController]
[Route("api/platform/bootstrap-owner")]
[AllowAnonymous]
[EnableRateLimiting("bootstrap")]
public sealed class OwnerBootstrapController(
    AppDbContext dbContext, IPasswordHasher passwordHasher, JwtTokenService tokenService,
    IConfiguration configuration, SecurityEventWriter securityEvents) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<object>> Status(CancellationToken ct)
    {
        var secretConfigured = !string.IsNullOrWhiteSpace(configuration["Platform:BootstrapSecret"]);
        var alreadyBootstrapped = await OwnerAlreadyExistsAsync(ct);
        return Ok(new { available = secretConfigured && !alreadyBootstrapped });
    }

    [HttpPost]
    public async Task<ActionResult<LoginResponse>> Bootstrap(BootstrapOwnerRequest request, CancellationToken ct)
    {
        var configuredSecret = configuration["Platform:BootstrapSecret"];
        if (string.IsNullOrWhiteSpace(configuredSecret))
        {
            return ValidationProblem("راه‌اندازی مالک هنوز پیکربندی نشده است.");
        }

        if (string.IsNullOrWhiteSpace(request.Secret) || !FixedTimeEquals(request.Secret, configuredSecret))
        {
            // The wrong-secret attempt is itself a security signal on an unauthenticated endpoint
            // that creates the most privileged account in the system — it belongs on the owner's
            // dashboard feed, not only in the log file.
            await securityEvents.WriteAsync(
                SecurityEventType.SuspiciousActivity,
                SecuritySeverity.Warning,
                "تلاش برای راه‌اندازی حساب مالک با کد نادرست.",
                mobile: string.IsNullOrWhiteSpace(request.Mobile) ? null : request.Mobile.Trim(),
                cancellationToken: ct);
            return Unauthorized(new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "کد راه‌اندازی نادرست است." });
        }

        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Mobile) || string.IsNullOrWhiteSpace(request.Password))
        {
            return ValidationProblem("نام، شمارهٔ همراه و رمز عبور الزامی است.");
        }

        // The most privileged account in the system must meet the same 8-character minimum as any
        // other password change — the bootstrap flow had no length rule at all.
        if (request.Password.Length < 8)
        {
            return ValidationProblem("رمز عبور باید حداقل ۸ کاراکتر باشد.");
        }

        // SERIALIZABLE + re-check inside: the exists-check and the insert are now one atomic unit.
        // A plain read-committed check-then-insert lets two concurrent bootstraps both see "no
        // owner" and both create one; serializable range-locks the read so the second transaction
        // blocks until the first commits, then sees the new owner and refuses.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await OwnerAlreadyExistsAsync(ct))
        {
            return Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "یک حساب مالک از قبل ثبت شده است." });
        }

        var mobileTaken = await dbContext.Users.AnyAsync(u => u.Mobile == request.Mobile && !u.IsDeleted, ct);
        if (mobileTaken)
        {
            return ValidationProblem("این شمارهٔ همراه قبلاً برای کاربر دیگری ثبت شده است.");
        }

        var hq = await dbContext.Organizations.FirstOrDefaultAsync(o => o.Level == OrganizationLevel.Headquarters, ct);
        if (hq is null)
        {
            hq = new Organization { Level = OrganizationLevel.Headquarters, Code = "HQ", Name = "دفتر مرکزی", IsActive = true };
            dbContext.Organizations.Add(hq);
        }

        // The owner role is identified by HOLDING Platform.Owner, not by IsSystemRole alone —
        // test seeders create many IsSystemRole roles with no permissions, and picking the first
        // of those would bootstrap a powerless "owner".
        var ownerRole = await dbContext.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(
                r => r.IsSystemRole && !r.IsDeleted
                    && r.RolePermissions.Any(p => p.Permission == Permissions.PlatformOwner && !p.IsDeleted),
                ct);
        if (ownerRole is null)
        {
            ownerRole = new Role { Name = "مالک نرم‌افزار", IsSystemRole = true };
            dbContext.Roles.Add(ownerRole);
            dbContext.RolePermissions.Add(new RolePermission { RoleId = ownerRole.Id, Permission = Permissions.PlatformOwner });
        }

        var user = new AppUser
        {
            FullName = request.FullName.Trim(),
            Mobile = request.Mobile.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            IsActive = true,
        };
        dbContext.Users.Add(user);
        dbContext.UserOrgRoles.Add(new UserOrgRole { UserId = user.Id, OrganizationId = hq.Id, RoleId = ownerRole.Id });

        // ONE SaveChanges inside the transaction — EF orders the inserts by dependency (HQ, role,
        // permission, user, membership), and a failure at any point rolls the whole bootstrap back.
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await securityEvents.WriteAsync(
            SecurityEventType.SensitiveSettingChanged,
            SecuritySeverity.Critical,
            $"حساب مالک پلتفرم راه‌اندازی شد (کاربر {request.Mobile.Trim()}).",
            mobile: request.Mobile.Trim(),
            cancellationToken: ct);

        var token = tokenService.CreateToken(user);
        return Ok(new LoginResponse(token, ExpiresInMinutes: 480));
    }

    private async Task<bool> OwnerAlreadyExistsAsync(CancellationToken ct) =>
        await dbContext.UserOrgRoles.AsNoTracking()
            .Where(m => !m.IsDeleted)
            .Include(m => m.Role).ThenInclude(r => r.RolePermissions)
            .AnyAsync(m => m.Role.RolePermissions.Any(p => p.Permission == Permissions.PlatformOwner && !p.IsDeleted), ct);

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
