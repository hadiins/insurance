namespace Aqsat.Api.Contracts;

/// <summary>The owner-side payment-gateway panel (docs/CUSTOMER-PORTAL-SPEC.md §3). The inquiry
/// fee the customer pays lands in the OWNER's account, so its gateway is a platform setting, not
/// an agency one. OwnerMerchantId is never returned in clear — only a mask and whether one is
/// configured at all, so a screenshot of the panel can never leak a billable credential (same
/// rule as ApiIrSettingsDto).</summary>
public sealed record PlatformPaymentSettingsDto(
    string Provider, bool Enabled, bool HasOwnerMerchantId, string? OwnerMerchantIdMasked,
    string? CallbackBaseUrl, decimal InquiryFeeToman, DateTimeOffset? UpdatedAt);

/// <summary>PUT body. OwnerMerchantId null or whitespace means "keep the stored one" — the panel
/// sends it that way whenever the field is untouched, so an ordinary save can never wipe a working
/// credential; replacing it is the only path that writes it (same contract as
/// UpdateApiIrSettingsRequest).</summary>
public sealed record UpdatePlatformPaymentSettingsRequest(
    string Provider, bool Enabled, string? OwnerMerchantId, string? CallbackBaseUrl, decimal InquiryFeeToman);