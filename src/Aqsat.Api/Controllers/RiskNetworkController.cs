using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Risk;
using Aqsat.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// The cross-agency half of the risk API (Phase 2B-1). Lookup needs Risk.NetworkRead and answers
/// only through NetworkRiskQueryService, which enforces the platform owner's switch and writes the
/// lookup's audit row. The settings endpoints are Platform.Owner-only (owner decision 2026-09-07)
/// and follow ApiIrSettingsController's singleton pattern: the row is created on first read and
/// never deleted.
/// </summary>
[ApiController]
[Route("api/risk/network")]
public sealed class RiskNetworkController(
    NetworkRiskQueryService queryService,
    AppDbContext dbContext,
    ICurrentUserContext currentUser,
    SecurityEventWriter securityEvents) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.RiskNetworkRead)]
    public async Task<ActionResult<NetworkRiskLookupApiDto>> Lookup(
        [FromQuery] string? nationalId, [FromQuery] string? plate, CancellationToken ct)
    {
        try
        {
            var dto = await queryService.LookupAsync(nationalId, plate, currentUser.UserId, currentUser.DisplayName, ct);
            return Ok(new NetworkRiskLookupApiDto(
                dto.IsEnabled,
                dto.QueryKind,
                dto.Results.Select(r => new NetworkRiskResultApiDto(
                    r.AgencyName, r.InsurerName, r.IsOwnAgency,
                    r.Score,
                    RiskLabels.LevelFa(r.RiskLevel), r.RiskLevel.ToString(),
                    RiskLabels.DecisionFa(r.Decision), r.Decision.ToString(),
                    r.OverdueCount, r.MaxDaysOverdue, r.ReturnedChequeCount, r.OnTimeRatePercent,
                    r.TenureMonths, r.SettledInstallmentCount, r.CalculatedAt)).ToList()));
        }
        catch (RiskAssessmentException ex)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = ex.Message });
        }
    }

    [HttpGet("settings")]
    [Authorize(Policy = Permissions.PlatformOwner)]
    public async Task<ActionResult<RiskNetworkSettingsDto>> GetSettings(CancellationToken ct)
    {
        // Tracked, not AsNoTracking: PUT mutates the returned row (same rationale as
        // ApiIrSettingsController.GetOrCreateAsync).
        var settings = await dbContext.RiskNetworkSettings.SingleOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new RiskNetworkSettings();
            dbContext.RiskNetworkSettings.Add(settings);
            await dbContext.SaveChangesAsync(ct);
        }

        return Ok(new RiskNetworkSettingsDto(
            settings.IsEnabled,
            settings.UpdatedAt == default ? null : settings.UpdatedAt));
    }

    [HttpPut("settings")]
    [Authorize(Policy = Permissions.PlatformOwner)]
    public async Task<ActionResult<RiskNetworkSettingsDto>> UpdateSettings(
        UpdateRiskNetworkSettingsRequest request, CancellationToken ct)
    {
        var settings = await dbContext.RiskNetworkSettings.SingleOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new RiskNetworkSettings();
            dbContext.RiskNetworkSettings.Add(settings);
        }

        settings.IsEnabled = request.IsEnabled;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedByUserId = currentUser.UserId;
        await dbContext.SaveChangesAsync(ct);

        // Sharing customer risk data across agencies is a privacy-relevant platform-wide switch —
        // its changes belong on the security feed (no AgencyId, so no AuditEntry).
        await securityEvents.WriteAsync(
            SecurityEventType.SensitiveSettingChanged, SecuritySeverity.Warning,
            $"اشتراک ریسک بین نمایندگی‌ها {(request.IsEnabled ? "فعال" : "غیرفعال")} شد",
            cancellationToken: ct);

        return Ok(new RiskNetworkSettingsDto(settings.IsEnabled, settings.UpdatedAt));
    }
}
