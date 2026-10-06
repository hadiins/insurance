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
/// The owner-side payment-gateway half of the platform panel — same audience as
/// ApiIrSettingsController (Platform.Owner only) and the same agency-blindness: collecting the
/// customer's inquiry fee (کارمزد استعلام) is an owner-account concern, never an agency one. The
/// agency's own down-payment gateway is configured per agency on OrgSettings instead. GET/PUT
/// only, no delete: the singleton row is created on first read and never removed. The merchant ID
/// is write-only through this API — GET returns a mask.
///
/// NB: the PlatformOwner policy lives on the ACTIONS, never on this class — authorize attributes
/// at class and action level are additive (an action-level [Authorize] does not replace the class
/// policy), and verification-status must stay readable by any signed-in operator: the issuance
/// wizard runs under agency accounts, and a 403 there silently fell back to "verification on",
/// routing every agent into step 4 no matter what the owner saved (incident 2026-10-06).
/// </summary>
[ApiController]
[Route("api/platform/payment")]
public sealed class PlatformPaymentController(
    AppDbContext dbContext, ICurrentUserContext currentUser, SecurityEventWriter securityEvents) : ControllerBase
{
    [HttpGet("settings")]
    [Authorize(Policy = Permissions.PlatformOwner)]
    public async Task<ActionResult<PlatformPaymentSettingsDto>> GetSettings(CancellationToken ct)
    {
        var settings = await GetOrCreateAsync(ct);
        return Ok(ToDto(settings));
    }

    /// <summary>Read-only, agency-safe view of the live verification switch for the issuance
    /// wizard (step 4). Any signed-in operator may read it — the panel's full payment settings
    /// stay Platform.Owner-only; only this one boolean is agency-visible, because every agent's
    /// wizard must know whether step 4 exists before creating the policy.</summary>
    [HttpGet("verification-status")]
    [Authorize]
    public async Task<ActionResult<VerificationStatusDto>> GetVerificationStatus(CancellationToken ct)
    {
        var settings = await GetOrCreateAsync(ct);
        return Ok(new VerificationStatusDto(settings.VerificationEnabled));
    }

    [HttpPut("settings")]
    [Authorize(Policy = Permissions.PlatformOwner)]
    public async Task<ActionResult<PlatformPaymentSettingsDto>> UpdateSettings(
        UpdatePlatformPaymentSettingsRequest request, CancellationToken ct)
    {
        var settings = await GetOrCreateAsync(ct);

        if (!Enum.TryParse<PaymentProvider>(request.Provider, ignoreCase: true, out var provider))
        {
            return ValidationProblem("درگاه پرداخت نامعتبر است.");
        }

        // Enabling a real gateway with no credential — new or already stored — would fail every
        // payment at the PSP later. Refuse now, at the moment the mistake is visible. Mock needs
        // no credential (that is its whole point).
        if (request.Enabled && provider != PaymentProvider.Mock &&
            string.IsNullOrWhiteSpace(request.OwnerMerchantId) &&
            string.IsNullOrWhiteSpace(settings.OwnerMerchantId))
        {
            return ValidationProblem("برای فعالسازی درگاه واقعی، شناسهٔ پذیرنده (Merchant ID) الزامی است.");
        }

        if (request.CallbackBaseUrl is { } url && !Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            return ValidationProblem("آدرس کالبک باید یک URL کامل و معتبر باشد (مثلاً https://api.example.ir).");
        }

        // The inquiry fee is collected through the owner's own gateway (owner decision 2026-09-01),
        // so its amount is set here, next to that gateway — never on any agency's settings.
        if (request.InquiryFeeToman < 0)
        {
            return ValidationProblem("کارمزد استعلام باید نامنفی باشد.");
        }

        settings.Provider = provider;
        settings.Enabled = request.Enabled;
        if (!string.IsNullOrWhiteSpace(request.OwnerMerchantId))
        {
            settings.OwnerMerchantId = request.OwnerMerchantId.Trim();
        }
        settings.CallbackBaseUrl = string.IsNullOrWhiteSpace(request.CallbackBaseUrl) ? null : request.CallbackBaseUrl.Trim();
        settings.InquiryFeeToman = request.InquiryFeeToman;
        if (request.VerificationEnabled is { } verificationEnabled)
        {
            settings.VerificationEnabled = verificationEnabled;
        }
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedByUserId = currentUser.UserId;
        await dbContext.SaveChangesAsync(ct);

        // Money moves through this configuration — the security feed is its only trail (the table
        // has no AgencyId, so no AuditEntry applies). Provider/enabled state only; the merchant ID
        // is a secret, "changed" suffices.
        await securityEvents.WriteAsync(
            SecurityEventType.SensitiveSettingChanged, SecuritySeverity.Warning,
            $"تنظیمات درگاه پرداخت مالک تغییر کرد (درگاه {provider}، وضعیت {(request.Enabled ? "فعال" : "غیرفعال")}، اعتبارسنجی {(settings.VerificationEnabled ? "فعال" : "غیرفعال")})",
            cancellationToken: ct);

        return Ok(ToDto(settings));
    }

    private async Task<PlatformPaymentSettings> GetOrCreateAsync(CancellationToken ct)
    {
        // Deliberately tracked (no AsNoTracking): UpdateSettings mutates the returned row and
        // relies on the change tracker to persist it. A no-tracking read here would make PUT onto
        // an already-existing row — the normal case after the panel's first read — a silent no-op
        // (SaveChangesAsync finds no changes) while still answering 200. Same pattern as
        // ApiIrSettingsController.
        var settings = await dbContext.PlatformPaymentSettings.SingleOrDefaultAsync(ct);
        if (settings is not null)
        {
            return settings;
        }

        settings = new PlatformPaymentSettings();
        dbContext.PlatformPaymentSettings.Add(settings);
        await dbContext.SaveChangesAsync(ct);
        return settings;
    }

    private static PlatformPaymentSettingsDto ToDto(PlatformPaymentSettings settings) =>
        new(
            settings.Provider.ToString(),
            settings.Enabled,
            !string.IsNullOrWhiteSpace(settings.OwnerMerchantId),
            Mask(settings.OwnerMerchantId),
            settings.CallbackBaseUrl,
            settings.InquiryFeeToman,
            settings.VerificationEnabled,
            settings.UpdatedAt == default ? null : settings.UpdatedAt);

    private static string? Mask(string merchantId) =>
        string.IsNullOrWhiteSpace(merchantId) ? null
        : merchantId.Length <= 8 ? "••••"
        : $"{merchantId[..4]}••••{merchantId[^4..]}";

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}