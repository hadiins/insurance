using Aqsat.Api.Contracts;
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
/// The owner-side payment-gateway half of the platform panel — same audience as
/// ApiIrSettingsController (Platform.Owner only) and the same agency-blindness: collecting the
/// customer's inquiry fee (کارمزد استعلام) is an owner-account concern, never an agency one. The
/// agency's own down-payment gateway is configured per agency on OrgSettings instead. GET/PUT
/// only, no delete: the singleton row is created on first read and never removed. The merchant ID
/// is write-only through this API — GET returns a mask.
/// </summary>
[ApiController]
[Route("api/platform/payment")]
[Authorize(Policy = Permissions.PlatformOwner)]
public sealed class PlatformPaymentController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<ActionResult<PlatformPaymentSettingsDto>> GetSettings(CancellationToken ct)
    {
        var settings = await GetOrCreateAsync(ct);
        return Ok(ToDto(settings));
    }

    [HttpPut("settings")]
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
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedByUserId = currentUser.UserId;
        await dbContext.SaveChangesAsync(ct);

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