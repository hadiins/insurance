using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// The platform owner's on/off switch for the public self-serve signup form (/signup) — replaces
/// editing Platform:AllowAgencySignup in appsettings (owner request 2026-09-08). Platform.Owner
/// only, same singleton-row pattern as RiskNetworkController's settings endpoints: created on
/// first read, never deleted. SignupController reads this row on every request, so a flip here
/// takes effect instantly.
/// </summary>
[ApiController]
[Route("api/platform/signup-settings")]
[Authorize(Policy = Permissions.PlatformOwner)]
public sealed class PlatformSignupSettingsController(
    AppDbContext dbContext, ICurrentUserContext currentUser, SecurityEventWriter securityEvents) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PlatformSignupSettingsDto>> GetSettings(CancellationToken ct)
    {
        var settings = await GetOrCreateAsync(ct);
        return Ok(ToDto(settings));
    }

    [HttpPut]
    public async Task<ActionResult<PlatformSignupSettingsDto>> UpdateSettings(
        UpdatePlatformSignupSettingsRequest request, CancellationToken ct)
    {
        var settings = await GetOrCreateAsync(ct);

        settings.AllowAgencySignup = request.AllowAgencySignup;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedByUserId = currentUser.UserId;
        await dbContext.SaveChangesAsync(ct);

        // Opening or closing the public signup gate changes the platform's attack surface —
        // exactly what the security feed tracks. The table has no AgencyId, hence no AuditEntry.
        await securityEvents.WriteAsync(
            SecurityEventType.SensitiveSettingChanged, SecuritySeverity.Info,
            $"ثبت‌نام عمومی نمایندگی‌ها {(request.AllowAgencySignup ? "فعال" : "غیرفعال")} شد",
            cancellationToken: ct);

        return Ok(ToDto(settings));
    }

    /// <summary>Tracked, not AsNoTracking: PUT mutates the returned row and relies on the change
    /// tracker to persist it (same rationale as ApiIrSettingsController.GetOrCreateAsync).</summary>
    private async Task<PlatformSignupSettings> GetOrCreateAsync(CancellationToken ct)
    {
        var settings = await dbContext.PlatformSignupSettings.SingleOrDefaultAsync(ct);
        if (settings is not null)
        {
            return settings;
        }

        settings = new PlatformSignupSettings();
        dbContext.PlatformSignupSettings.Add(settings);
        await dbContext.SaveChangesAsync(ct);
        return settings;
    }

    private static PlatformSignupSettingsDto ToDto(PlatformSignupSettings settings) =>
        new(settings.AllowAgencySignup, settings.UpdatedAt == default ? null : settings.UpdatedAt);
}
