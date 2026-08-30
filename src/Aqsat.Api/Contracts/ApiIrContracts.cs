namespace Aqsat.Api.Contracts;

/// <summary>The api.ir half of the platform panel. ApiKey is never returned in the clear — only a
/// mask and whether one is configured at all, so a screenshot of the panel can never leak a
/// billable credential.</summary>
public sealed record ApiIrSettingsDto(bool AllowPaidEndpoints, bool HasApiKey, string? ApiKeyMasked, DateTimeOffset? UpdatedAt);

/// <summary>PUT body. ApiKey null or whitespace means "keep the stored one" — the panel sends it
/// that way whenever the field is untouched, so an ordinary save can never wipe a working key;
/// replacing the key is the only path that writes it.</summary>
public sealed record UpdateApiIrSettingsRequest(bool AllowPaidEndpoints, string? ApiKey);