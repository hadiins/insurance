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
/// The api.ir half of the platform panel — same audience as PlatformUpdatesController
/// (Platform.Owner only, docs/UPDATE-SYSTEM.md rule 1) and the same agency-blindness: api.ir is a
/// platform-wide vendor account, never an agency one. GET/PUT only, no delete: the singleton row is
/// created on first read and never removed, so the client always has a settings source (worst case
/// it falls back to configuration). Saving here takes effect within seconds — ApiIrClient re-reads
/// the row through a short-lived cache, no container recreate involved.
/// </summary>
[ApiController]
[Route("api/platform/apiir")]
[Authorize(Policy = Permissions.PlatformOwner)]
public sealed class ApiIrSettingsController(
    AppDbContext dbContext, ICurrentUserContext currentUser, SecurityEventWriter securityEvents) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<ActionResult<ApiIrSettingsDto>> GetSettings(CancellationToken ct)
    {
        var settings = await GetOrCreateAsync(ct);
        return Ok(new ApiIrSettingsDto(
            settings.AllowPaidEndpoints ?? false,
            !string.IsNullOrWhiteSpace(settings.ApiKey),
            Mask(settings.ApiKey),
            settings.UpdatedAt == default ? null : settings.UpdatedAt));
    }

    [HttpPut("settings")]
    public async Task<ActionResult<ApiIrSettingsDto>> UpdateSettings(UpdateApiIrSettingsRequest request, CancellationToken ct)
    {
        var settings = await GetOrCreateAsync(ct);
        settings.AllowPaidEndpoints = request.AllowPaidEndpoints;
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            // A pasted whole Authorization header ("Bearer eyJ…") would double up on the wire and
            // 401 at api.ir — store the bare key only.
            var trimmed = request.ApiKey.Trim();
            settings.ApiKey = trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? trimmed["Bearer ".Length..].Trim()
                : trimmed;
        }
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedByUserId = currentUser.UserId;
        await dbContext.SaveChangesAsync(ct);

        // Platform-wide vendor account settings — no AuditEntry row exists for this table (it has
        // no AgencyId to hang one on), so the security feed is the only trail. Values deliberately
        // not logged: the API key is a secret, "changed" is all anyone needs (rule 30's spirit).
        await securityEvents.WriteAsync(
            SecurityEventType.SensitiveSettingChanged, SecuritySeverity.Warning,
            $"تنظیمات اتصال api.ir تغییر کرد (اجازهٔ سرویس‌های پولی: {(request.AllowPaidEndpoints ? "فعال" : "غیرفعال")})",
            cancellationToken: ct);

        return Ok(new ApiIrSettingsDto(
            settings.AllowPaidEndpoints ?? false,
            !string.IsNullOrWhiteSpace(settings.ApiKey),
            Mask(settings.ApiKey),
            settings.UpdatedAt));
    }

    private async Task<ApiIrSettings> GetOrCreateAsync(CancellationToken ct)
    {
        // Deliberately tracked (no AsNoTracking): UpdateSettings mutates the returned row and relies
        // on the change tracker to persist it. A no-tracking read here would make PUT onto an
        // already-existing row — the normal case after the panel's first read — a silent no-op
        // (SaveChangesAsync finds no changes) while still answering 200.
        var settings = await dbContext.ApiIrSettings.SingleOrDefaultAsync(ct);
        if (settings is not null)
        {
            return settings;
        }

        settings = new ApiIrSettings();
        dbContext.ApiIrSettings.Add(settings);
        await dbContext.SaveChangesAsync(ct);
        return settings;
    }

    private static string? Mask(string apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) ? null
        : apiKey.Length <= 8 ? "••••"
        : $"{apiKey[..4]}••••{apiKey[^4..]}";
}